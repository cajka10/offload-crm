using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Abstractions;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.ProjectStatuses;
using Offload.Crm.Application.Users;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Application.Projects;

public interface IProjectService
{
    /// <summary>Search (name, customer name; D-16), filters, sort (<see cref="ProjectSort"/>) and paging.</summary>
    Task<PagedResult<ProjectListItem>> ListAsync(ProjectListQuery query, CancellationToken ct = default);

    /// <summary>Projects for autocompletes, optionally of one customer.</summary>
    Task<IReadOnlyList<ProjectLookupItem>> LookupAsync(string? search, int? customerId = null, int take = 20, CancellationToken ct = default);

    Task<ProjectDetail> GetAsync(int id, CancellationToken ct = default);

    Task<ProjectDetail> CreateAsync(ProjectInput input, CancellationToken ct = default);

    /// <summary>Updates; a status change also logs a Note activity (D-14).</summary>
    Task<ProjectDetail> UpdateAsync(int id, ProjectInput input, CancellationToken ct = default);

    /// <summary>Inline status change; logs a Note activity (D-14).</summary>
    Task<ProjectDetail> ChangeStatusAsync(int id, int statusId, CancellationToken ct = default);

    /// <summary>Deletes the project and its tasks; its activities are kept (ProjectId = null) (D-07).</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Newest-first feed of creation, activities and task completions (D-14).</summary>
    Task<IReadOnlyList<TimelineItem>> GetTimelineAsync(int id, CancellationToken ct = default);
}

public sealed record ProjectLookupItem(int Id, string Name, int CustomerId, string CustomerName);

public sealed class ProjectService(IAppDbContextFactory dbFactory, ICurrentUser currentUser, TimeProvider clock) : IProjectService
{
    private static readonly SortMap<Project> Sorts = new SortMap<Project>(ProjectSort.Name, p => p.Id)
        .Add(ProjectSort.Name, p => p.Name)
        .Add(ProjectSort.Customer, p => p.Customer.CompanyName)
        .Add(ProjectSort.Status, p => p.Status.SortOrder)
        .AddEnum(ProjectSort.Priority, p => p.Priority)
        .Add(ProjectSort.Responsible, p => p.ResponsibleUser != null ? p.ResponsibleUser.FullName : null)
        .Add(ProjectSort.Start, p => p.StartDate)
        .Add(ProjectSort.Target, p => p.TargetDate)
        .Add(ProjectSort.EstimatedValue, p => p.EstimatedValue)
        .Add(ProjectSort.OpenTasks, p => p.Tasks.Count(t => t.Status != TaskItemStatus.Done))
        .Add(ProjectSort.Created, p => p.CreatedAt);

    public async Task<PagedResult<ProjectListItem>> ListAsync(ProjectListQuery query, CancellationToken ct = default)
    {
        Guard.EnumDefined(query.Priority, "priority");

        await using var db = await dbFactory.CreateAsync(ct);
        IQueryable<Project> projects = db.Projects.AsNoTracking();

        // BUG-017: explicitly chosen statuses are shown as chosen; "active only" applies only without them.
        if (query.ActiveOnly && query.StatusIds is not { Count: > 0 })
        {
            projects = projects.Where(Predicates.ActiveProject);
        }

        if (query.StatusIds is { Count: > 0 } statusIds)
        {
            projects = projects.Where(p => statusIds.Contains(p.StatusId));
        }

        if (query.CustomerId is { } customerId)
        {
            projects = projects.Where(p => p.CustomerId == customerId);
        }

        if (query.Priority is { } priority)
        {
            projects = projects.Where(p => p.Priority == priority);
        }

        if (InputText.Clean(query.ResponsibleUserId) is { } responsible)
        {
            projects = projects.Where(p => p.ResponsibleUserId == responsible);
        }

        if (query.NormalizedSearch is { } term)
        {
            projects = projects.Where(p => p.Name.ToLower().Contains(term) || p.Customer.CompanyName.ToLower().Contains(term));
        }

        return await Sorts.Apply(projects, query.Sort, query.Descending).ToPagedResultAsync(
            query,
            q => q.Select(p => new ProjectListItem(
                p.Id,
                p.Name,
                p.CustomerId,
                p.Customer.CompanyName,
                p.StatusId,
                p.Status.Name,
                p.Status.Color,
                p.Status.IsClosed,
                p.Priority,
                p.ResponsibleUserId,
                p.ResponsibleUser != null ? p.ResponsibleUser.FullName : null,
                p.ResponsibleUser != null && p.ResponsibleUser.IsActive,
                p.StartDate,
                p.TargetDate,
                p.EstimatedValue,
                p.Tasks.Count(t => t.Status != TaskItemStatus.Done),
                p.CreatedAt)),
            ct);
    }

    public async Task<IReadOnlyList<ProjectLookupItem>> LookupAsync(string? search, int? customerId = null, int take = 20, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var term = InputText.SearchTerm(search);
        return await db.Projects.AsNoTracking()
            .Where(p => customerId == null || p.CustomerId == customerId)
            .Where(p => term == null || p.Name.ToLower().Contains(term) || p.Customer.CompanyName.ToLower().Contains(term))
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Take(Math.Clamp(take, 1, 100))
            .Select(p => new ProjectLookupItem(p.Id, p.Name, p.CustomerId, p.Customer.CompanyName))
            .ToListAsync(ct);
    }

    public async Task<ProjectDetail> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        return await GetDetailAsync(db, id, ct);
    }

    public async Task<ProjectDetail> CreateAsync(ProjectInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        await ValidateAsync(db, input, null, ct);

        var project = new Project();
        Apply(project, input);
        project.StatusId = input.StatusId ?? (await ProjectStatusService.GetDefaultAsync(db, ct)).Id;
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        return await GetDetailAsync(db, project.Id, ct);
    }

    public async Task<ProjectDetail> UpdateAsync(int id, ProjectInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var project = await db.Projects.Include(p => p.Status).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Project", id);

        await ValidateAsync(db, input, project, ct);

        var oldCustomerId = project.CustomerId;
        Apply(project, input);
        if (input.StatusId is { } statusId && statusId != project.StatusId)
        {
            await SetStatusAsync(db, project, statusId, ct);
        }

        if (project.CustomerId != oldCustomerId)
        {
            // Keep D-12 consistency: the project's activities belong to its (new) customer.
            var activities = await db.Activities.Where(a => a.ProjectId == id).ToListAsync(ct);
            activities.ForEach(a => a.CustomerId = project.CustomerId);

            // D-22.4: the change is logged like a status change.
            var names = await db.Customers.Where(c => c.Id == oldCustomerId || c.Id == project.CustomerId)
                .ToDictionaryAsync(c => c.Id, c => c.CompanyName, ct);
            await AddNoteAsync(db, project, "Customer changed", $"Customer changed: {names.GetValueOrDefault(oldCustomerId)} → {names.GetValueOrDefault(project.CustomerId)}", ct);
        }

        await db.SaveChangesAsync(ct);
        return await GetDetailAsync(db, id, ct);
    }

    public async Task<ProjectDetail> ChangeStatusAsync(int id, int statusId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var project = await db.Projects.Include(p => p.Status).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Project", id);

        if (statusId != project.StatusId)
        {
            if (!await db.ProjectStatuses.AnyAsync(s => s.Id == statusId, ct))
            {
                throw new AppValidationException("statusId", "Select a valid status.");
            }

            await SetStatusAsync(db, project, statusId, ct);
            await db.SaveChangesAsync(ct);
        }

        return await GetDetailAsync(db, id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Project", id);

        // Tasks cascade; activities keep their customer link (FK SetNull) (D-07).
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TimelineItem>> GetTimelineAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new { p.Name, p.CreatedAt })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Project", id);

        var activities = (await db.Activities.AsNoTracking().Where(a => a.ProjectId == id)
                .Select(a => new { a.Date, a.Subject, a.Type, a.Description, Author = a.Author.FullName, a.Id })
                .ToListAsync(ct))
            .Select(a => new TimelineItem(
                a.Date,
                TimelineKind.Activity,
                a.Subject ?? DisplayNames.Of(a.Type), // BUG-027: "Phone call", not "Call"
                a.Description,
                a.Author,
                a.Id,
                null,
                a.Type))
            .ToList();

        var completions = await db.Tasks.AsNoTracking().Where(t => t.ProjectId == id && t.CompletedAt != null)
            .Select(t => new TimelineItem(
                t.CompletedAt!.Value,
                TimelineKind.TaskCompleted,
                "Task completed: " + t.Title,
                null,
                t.AssignedUser != null ? t.AssignedUser.FullName : null,
                null,
                t.Id,
                null))
            .ToListAsync(ct);

        return activities
            .Concat(completions)
            .Append(new TimelineItem(project.CreatedAt, TimelineKind.ProjectCreated, "Project created", project.Name, null, null, null, null))
            .OrderByDescending(i => i.At)
            .ToList();
    }

    private async Task SetStatusAsync(IAppDbContext db, Project project, int newStatusId, CancellationToken ct)
    {
        var newStatus = await db.ProjectStatuses.FirstAsync(s => s.Id == newStatusId, ct);
        var authorId = await currentUser.GetUserIdAsync(ct) ?? throw new SessionExpiredException();
        var oldName = project.Status?.Name ?? (await db.ProjectStatuses.FirstAsync(s => s.Id == project.StatusId, ct)).Name;

        project.Status = newStatus;
        project.StatusId = newStatus.Id;

        // D-14/D-19: the status history is a Note activity.
        await AddNoteAsync(db, project, "Status changed", $"Status changed: {oldName} → {newStatus.Name}", ct, authorId);
    }

    private async Task AddNoteAsync(IAppDbContext db, Project project, string subject, string description, CancellationToken ct, string? authorId = null)
    {
        db.Activities.Add(new Activity
        {
            Type = ActivityType.Note,
            Date = clock.GetUtcNow().UtcDateTime,
            Subject = subject,
            Description = description,
            ProjectId = project.Id,
            CustomerId = project.CustomerId,
            AuthorId = authorId ?? await currentUser.GetUserIdAsync(ct) ?? throw new SessionExpiredException(),
        });
    }

    private static async Task ValidateAsync(IAppDbContext db, ProjectInput input, Project? existing, CancellationToken ct)
    {
        input.Normalize();
        var errors = ModelValidator.Validate(input);

        if (input.CustomerId is { } customerId && !await db.Customers.AnyAsync(c => c.Id == customerId, ct))
        {
            errors[nameof(ProjectInput.CustomerId)] = ["Select an existing customer."];
        }

        if (input.StatusId is { } statusId && !await db.ProjectStatuses.AnyAsync(s => s.Id == statusId, ct))
        {
            errors[nameof(ProjectInput.StatusId)] = ["Select a valid status."];
        }

        if (await UserRules.ValidateAssigneeAsync(db, input.ResponsibleUserId, existing?.ResponsibleUserId, ct) is { } userError)
        {
            errors[nameof(ProjectInput.ResponsibleUserId)] = [userError];
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }
    }

    private static void Apply(Project project, ProjectInput input)
    {
        project.Name = input.Name!;
        project.Description = input.Description;
        project.CustomerId = input.CustomerId!.Value;
        project.Priority = input.Priority;
        project.ResponsibleUserId = input.ResponsibleUserId;
        project.StartDate = input.StartDate;
        project.TargetDate = input.TargetDate;
        project.EstimatedValue = input.EstimatedValue;
        project.ActualValue = input.ActualValue;
        project.Notes = input.Notes;
    }

    private static async Task<ProjectDetail> GetDetailAsync(IAppDbContext db, int id, CancellationToken ct) =>
        await db.Projects.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new ProjectDetail(
                p.Id,
                p.Name,
                p.Description,
                p.CustomerId,
                p.Customer.CompanyName,
                p.StatusId,
                p.Status.Name,
                p.Status.Color,
                p.Status.IsActive,
                p.Status.IsClosed,
                p.Priority,
                p.ResponsibleUserId,
                p.ResponsibleUser != null ? p.ResponsibleUser.FullName : null,
                p.ResponsibleUser != null && p.ResponsibleUser.IsActive,
                p.StartDate,
                p.TargetDate,
                p.EstimatedValue,
                p.ActualValue,
                p.Notes,
                p.Tasks.Count,
                p.Tasks.Count(t => t.Status != TaskItemStatus.Done),
                p.Activities.Count,
                p.CreatedAt,
                p.UpdatedAt))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Project", id);
}
