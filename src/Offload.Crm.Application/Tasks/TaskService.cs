using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Abstractions;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Users;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Application.Tasks;

public interface ITaskService
{
    /// <summary>Search (title; D-16), filters, sort (<see cref="TaskSort"/>) and paging.</summary>
    Task<PagedResult<TaskDto>> ListAsync(TaskListQuery query, CancellationToken ct = default);

    /// <summary>
    /// Tasks matching the filters for the Kanban board (no paging, capped at <see cref="TaskService.BoardLimit"/>).
    /// With <paramref name="recentDoneOnly"/>, Done tasks completed more than 30 days ago are left out and counted (D-22.1).
    /// </summary>
    Task<TaskBoard> GetBoardAsync(TaskListQuery query, bool recentDoneOnly, CancellationToken ct = default);

    Task<TaskDto> GetAsync(int id, CancellationToken ct = default);

    Task<TaskDto> CreateAsync(TaskInput input, CancellationToken ct = default);

    Task<TaskDto> UpdateAsync(int id, TaskInput input, CancellationToken ct = default);

    /// <summary>Changes only the status; CompletedAt follows D-13.</summary>
    Task<TaskDto> ChangeStatusAsync(int id, TaskItemStatus status, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class TaskService(IAppDbContextFactory dbFactory, ICurrentUser currentUser, TimeProvider clock) : ITaskService
{
    public const int BoardLimit = 500;
    public const int DoneWindowDays = 30;

    private static readonly SortMap<TaskItem> Sorts = new SortMap<TaskItem>(TaskSort.Due, t => t.Id)
        .Add(TaskSort.Title, t => t.Title)
        .Add(TaskSort.Project, t => t.Project.Name)
        .Add(TaskSort.Customer, t => t.Project.Customer.CompanyName)
        .AddEnum(TaskSort.Status, t => t.Status)
        .AddEnum(TaskSort.Priority, t => t.Priority)
        .Add(TaskSort.Assignee, t => t.AssignedUser != null ? t.AssignedUser.FullName : null)
        .Add(TaskSort.Due, t => t.DueDate)
        .Add(TaskSort.Created, t => t.CreatedAt);

    private static readonly Expression<Func<TaskItem, TaskDto>> ToDto = t => new TaskDto(
        t.Id,
        t.Title,
        t.Description,
        t.ProjectId,
        t.Project.Name,
        t.Project.CustomerId,
        t.Project.Customer.CompanyName,
        t.Status,
        t.Priority,
        t.AssignedUserId,
        t.AssignedUser != null ? t.AssignedUser.FullName : null,
        t.AssignedUser != null && t.AssignedUser.IsActive,
        t.DueDate,
        t.CompletedAt,
        t.CreatedAt,
        t.UpdatedAt);

    public async Task<PagedResult<TaskDto>> ListAsync(TaskListQuery query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var tasks = await FilterAsync(db, query, ct);
        return await Sorts.Apply(tasks, query.Sort, query.Descending).ToPagedResultAsync(query, q => q.Select(ToDto), ct);
    }

    public async Task<TaskBoard> GetBoardAsync(TaskListQuery query, bool recentDoneOnly, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var tasks = await FilterAsync(db, query, ct);

        var olderDone = 0;
        if (recentDoneOnly)
        {
            // D-22.1: the Done column only shows tasks completed in the last 30 days.
            var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-DoneWindowDays);
            olderDone = await tasks.CountAsync(t => t.Status == TaskItemStatus.Done && (t.CompletedAt == null || t.CompletedAt < cutoff), ct);
            tasks = tasks.Where(t => t.Status != TaskItemStatus.Done || t.CompletedAt >= cutoff);
        }

        var total = await tasks.CountAsync(ct);
        var items = await Sorts.Apply(tasks, query.Sort ?? TaskSort.Priority, query.Sort is null || query.Descending)
            .Take(BoardLimit)
            .Select(ToDto)
            .ToListAsync(ct);
        return new TaskBoard(items, total, olderDone);
    }

    public async Task<TaskDto> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        return await GetDtoAsync(db, id, ct);
    }

    public async Task<TaskDto> CreateAsync(TaskInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        await ValidateAsync(db, input, null, ct);

        var task = new TaskItem();
        Apply(task, input);
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        return await GetDtoAsync(db, task.Id, ct);
    }

    public async Task<TaskDto> UpdateAsync(int id, TaskInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Task", id);

        await ValidateAsync(db, input, task, ct);
        Apply(task, input);
        await db.SaveChangesAsync(ct);
        return await GetDtoAsync(db, id, ct);
    }

    public async Task<TaskDto> ChangeStatusAsync(int id, TaskItemStatus status, CancellationToken ct = default)
    {
        Guard.EnumDefined<TaskItemStatus>(status, "status");

        await using var db = await dbFactory.CreateAsync(ct);
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Task", id);
        task.ChangeStatus(status, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return await GetDtoAsync(db, id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Task", id);
        db.Tasks.Remove(task);
        await db.SaveChangesAsync(ct);
    }

    private async Task<IQueryable<TaskItem>> FilterAsync(IAppDbContext db, TaskListQuery query, CancellationToken ct)
    {
        Guard.EnumsDefined(query.Statuses, "status");
        Guard.EnumDefined(query.Priority, "priority");

        IQueryable<TaskItem> tasks = db.Tasks.AsNoTracking();

        if (query.Statuses is { Count: > 0 } statuses)
        {
            var list = statuses.Distinct().ToList();
            tasks = list.Count == TaskListQuery.OpenStatuses.Count && !list.Except(TaskListQuery.OpenStatuses).Any()
                ? tasks.Where(Predicates.OpenTask) // "open tasks" = the shared D-03 rule
                : tasks.Where(t => list.Contains(t.Status));
        }

        if (query.Priority is { } priority)
        {
            tasks = tasks.Where(t => t.Priority == priority);
        }

        switch (query.Assignee?.Trim())
        {
            case null or "":
                break;
            case var a when a.Equals(TaskListQuery.AssigneeUnassigned, StringComparison.OrdinalIgnoreCase):
                tasks = tasks.Where(t => t.AssignedUserId == null);
                break;
            case var a when a.Equals(TaskListQuery.AssigneeMe, StringComparison.OrdinalIgnoreCase):
                var me = await currentUser.GetUserIdAsync(ct);
                tasks = tasks.Where(t => t.AssignedUserId == me);
                break;
            case var userId:
                tasks = tasks.Where(t => t.AssignedUserId == userId);
                break;
        }

        if (query.ProjectId is { } projectId)
        {
            tasks = tasks.Where(t => t.ProjectId == projectId);
        }

        if (query.CustomerId is { } customerId)
        {
            tasks = tasks.Where(t => t.Project.CustomerId == customerId);
        }

        if (query.OverdueOnly)
        {
            tasks = tasks.Where(Predicates.OverdueTask(AppTime.Today(clock)));
        }

        if (query.NormalizedSearch is { } term)
        {
            tasks = tasks.Where(t => t.Title.ToLower().Contains(term));
        }

        return tasks;
    }

    private static async Task ValidateAsync(IAppDbContext db, TaskInput input, TaskItem? existing, CancellationToken ct)
    {
        input.Normalize();
        var errors = ModelValidator.Validate(input);

        if (input.ProjectId is { } projectId && !await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            errors[nameof(TaskInput.ProjectId)] = ["Select an existing project."];
        }

        if (await UserRules.ValidateAssigneeAsync(db, input.AssignedUserId, existing?.AssignedUserId, ct) is { } userError)
        {
            errors[nameof(TaskInput.AssignedUserId)] = [userError];
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }
    }

    private void Apply(TaskItem task, TaskInput input)
    {
        task.Title = input.Title!;
        task.Description = input.Description;
        task.ProjectId = input.ProjectId!.Value;
        task.Priority = input.Priority;
        task.AssignedUserId = input.AssignedUserId;
        task.DueDate = input.DueDate;
        task.ChangeStatus(input.Status, clock.GetUtcNow().UtcDateTime);
    }

    private static async Task<TaskDto> GetDtoAsync(IAppDbContext db, int id, CancellationToken ct) =>
        await db.Tasks.AsNoTracking().Where(t => t.Id == id).Select(ToDto).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Task", id);
}
