using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Abstractions;
using Offload.Crm.Application.Activities;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Tasks;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Application.Dashboard;

public sealed record DashboardKpis(int ActiveCustomers, int ActiveProjects, int OpenTasks, int OverdueTasks, decimal PipelineValue);

public sealed record StatusBar(int StatusId, string Name, string Color, bool IsActive, bool IsClosed, int ProjectCount);

public enum DeadlineKind
{
    Task,
    Project,
}

/// <summary>An upcoming (or overdue) task due date or project target date (D-03/D-23).</summary>
public sealed record DeadlineItem(
    DeadlineKind Kind,
    int Id,
    string Title,
    int ProjectId,
    string ProjectName,
    string CustomerName,
    DateOnly Date,
    string? PersonName,
    int DaysFromToday)
{
    public bool IsOverdue => DaysFromToday < 0;
}

public sealed record RecentCustomer(int Id, string Name, CustomerStatus Status, DateTime UpdatedAt);

public sealed record RecentProject(int Id, string Name, string StatusName, string StatusColor, DateTime UpdatedAt);

public sealed record RecentlyUpdated(IReadOnlyList<RecentCustomer> Customers, IReadOnlyList<RecentProject> Projects);

/// <summary>Everything on the dashboard for the current user (<c>GET /api/dashboard</c>).</summary>
public sealed record DashboardDto(
    DateOnly Today,
    DashboardKpis Kpis,
    IReadOnlyList<StatusBar> ProjectsByStatus,
    IReadOnlyList<DeadlineItem> UpcomingDeadlines,
    IReadOnlyList<TaskDto> MyOpenTasks,
    IReadOnlyList<ActivityDto> RecentActivities,
    RecentlyUpdated RecentlyUpdated);

/// <summary>
/// Dashboard queries (D-23). Every number uses the same <see cref="Predicates"/> as the list it links to. Each
/// widget has its own method so the UI can load/fail them independently.
/// </summary>
public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken ct = default);

    Task<DashboardKpis> GetKpisAsync(CancellationToken ct = default);

    Task<IReadOnlyList<StatusBar>> GetProjectsByStatusAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DeadlineItem>> GetUpcomingDeadlinesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<TaskDto>> GetMyOpenTasksAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ActivityDto>> GetRecentActivitiesAsync(CancellationToken ct = default);

    Task<RecentlyUpdated> GetRecentlyUpdatedAsync(CancellationToken ct = default);
}

public sealed class DashboardService(
    IAppDbContextFactory dbFactory,
    ITaskService tasks,
    ICurrentUser currentUser,
    TimeProvider clock) : IDashboardService
{
    public const int DeadlineDays = 14;
    public const int WidgetRows = 10;

    public async Task<DashboardDto> GetAsync(CancellationToken ct = default) => new(
        AppTime.Today(clock),
        await GetKpisAsync(ct),
        await GetProjectsByStatusAsync(ct),
        await GetUpcomingDeadlinesAsync(ct),
        await GetMyOpenTasksAsync(ct),
        await GetRecentActivitiesAsync(ct),
        await GetRecentlyUpdatedAsync(ct));

    public async Task<DashboardKpis> GetKpisAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var today = AppTime.Today(clock);

        var activeCustomers = await db.Customers.CountAsync(Predicates.ActiveCustomer, ct);
        var projects = await db.Projects.Where(Predicates.ActiveProject)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Pipeline = g.Sum(p => p.EstimatedValue ?? 0) })
            .FirstOrDefaultAsync(ct);
        var openTasks = await db.Tasks.CountAsync(Predicates.OpenTask, ct);
        var overdueTasks = await db.Tasks.CountAsync(Predicates.OverdueTask(today), ct);

        return new DashboardKpis(activeCustomers, projects?.Count ?? 0, openTasks, overdueTasks, projects?.Pipeline ?? 0);
    }

    public async Task<IReadOnlyList<StatusBar>> GetProjectsByStatusAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        return await db.ProjectStatuses.AsNoTracking()
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .Select(s => new StatusBar(s.Id, s.Name, s.Color, s.IsActive, s.IsClosed, s.Projects.Count))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DeadlineItem>> GetUpcomingDeadlinesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var today = AppTime.Today(clock);
        var until = today.AddDays(DeadlineDays);

        // Open tasks and non-closed projects due by today+14 (overdue included), overdue first then by date.
        var taskRows = await db.Tasks.AsNoTracking()
            .Where(Predicates.OpenTask)
            .Where(t => t.DueDate != null && t.DueDate <= until)
            .OrderBy(t => t.DueDate).ThenBy(t => t.Id)
            .Take(WidgetRows)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.ProjectId,
                ProjectName = t.Project.Name,
                CustomerName = t.Project.Customer.CompanyName,
                Date = t.DueDate!.Value,
                Person = t.AssignedUser != null ? t.AssignedUser.FullName : null,
            })
            .ToListAsync(ct);
        var projectRows = await db.Projects.AsNoTracking()
            .Where(p => !p.Status.IsClosed && p.TargetDate != null && p.TargetDate <= until)
            .OrderBy(p => p.TargetDate).ThenBy(p => p.Id)
            .Take(WidgetRows)
            .Select(p => new
            {
                p.Id,
                p.Name,
                CustomerName = p.Customer.CompanyName,
                Date = p.TargetDate!.Value,
                Person = p.ResponsibleUser != null ? p.ResponsibleUser.FullName : null,
            })
            .ToListAsync(ct);

        return taskRows
            .Select(t => new DeadlineItem(DeadlineKind.Task, t.Id, t.Title, t.ProjectId, t.ProjectName, t.CustomerName, t.Date, t.Person, t.Date.DayNumber - today.DayNumber))
            .Concat(projectRows.Select(p => new DeadlineItem(DeadlineKind.Project, p.Id, p.Name, p.Id, p.Name, p.CustomerName, p.Date, p.Person, p.Date.DayNumber - today.DayNumber)))
            .OrderBy(d => d.Date).ThenBy(d => d.Kind).ThenBy(d => d.Id)
            .Take(WidgetRows)
            .ToList();
    }

    public async Task<IReadOnlyList<TaskDto>> GetMyOpenTasksAsync(CancellationToken ct = default)
    {
        if (await currentUser.GetUserIdAsync(ct) is null)
        {
            return [];
        }

        // Same query as the "View all" link /tasks?assignee=me (open tasks, due date ascending, none last).
        var result = await tasks.ListAsync(
            new TaskListQuery
            {
                Assignee = TaskListQuery.AssigneeMe,
                Statuses = TaskListQuery.OpenStatuses,
                Sort = TaskSort.Due,
                PageSize = WidgetRows,
            },
            ct);
        return result.Items;
    }

    public async Task<IReadOnlyList<ActivityDto>> GetRecentActivitiesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.Activities.AsNoTracking()
            .Where(a => a.Date <= now) // future-dated activities are not "recent" (D-23)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.Id)
            .Take(WidgetRows)
            .Select(a => new ActivityDto(
                a.Id,
                a.Type,
                a.Date,
                a.Subject,
                a.Description,
                a.CustomerId,
                a.Customer != null ? a.Customer.CompanyName : null,
                a.ProjectId,
                a.Project != null ? a.Project.Name : null,
                a.AuthorId,
                a.Author.FullName,
                a.CreatedAt,
                a.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<RecentlyUpdated> GetRecentlyUpdatedAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var customers = await db.Customers.AsNoTracking()
            .OrderByDescending(c => c.UpdatedAt).ThenByDescending(c => c.Id)
            .Take(5)
            .Select(c => new RecentCustomer(c.Id, c.CompanyName, c.Status, c.UpdatedAt))
            .ToListAsync(ct);
        var projects = await db.Projects.AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt).ThenByDescending(p => p.Id)
            .Take(5)
            .Select(p => new RecentProject(p.Id, p.Name, p.Status.Name, p.Status.Color, p.UpdatedAt))
            .ToListAsync(ct);
        return new RecentlyUpdated(customers, projects);
    }
}
