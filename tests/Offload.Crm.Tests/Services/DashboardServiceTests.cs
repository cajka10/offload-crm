using Offload.Crm.Application.Activities;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Customers;
using Offload.Crm.Application.Dashboard;
using Offload.Crm.Application.Projects;
using Offload.Crm.Application.Tasks;
using Offload.Crm.Domain.Enums;
using Offload.Crm.Tests.Infrastructure;

namespace Offload.Crm.Tests.Services;

/// <summary>T-23/T-28: every KPI equals the totalCount of its click-through list (D-23), plus boundaries.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class DashboardServiceTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new();
    private DashboardService _dashboard = null!;
    private TaskService _tasks = null!;
    private DateOnly _today;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _user.UserId = (await db.AddUserAsync("me@test.local")).Id;
        _tasks = new TaskService(db, _user, db.Clock);
        _dashboard = new DashboardService(db, _tasks, _user, db.Clock);
        _today = AppTime.Today(db.Clock);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedAsync()
    {
        var projects = new ProjectService(db, _user, db.Clock);
        var active = await db.AddCustomerAsync("Active 1", status: CustomerStatus.Active);
        await db.AddCustomerAsync("Active 2", status: CustomerStatus.Active);
        await db.AddCustomerAsync("Lead", status: CustomerStatus.Lead);
        await db.AddCustomerAsync("Inactive", status: CustomerStatus.Inactive);

        var p1 = await projects.CreateAsync(new ProjectInput { Name = "Open A", CustomerId = active.Id, EstimatedValue = 1000 });
        await projects.CreateAsync(new ProjectInput { Name = "Open B", CustomerId = active.Id }); // null value counts as 0
        await projects.CreateAsync(new ProjectInput { Name = "Delivered", CustomerId = active.Id, EstimatedValue = 5000, StatusId = await db.StatusIdAsync("Delivered") });

        await db.AddTaskAsync(p1.Id, "Overdue", due: _today.AddDays(-1), assigneeId: _user.UserId);
        await db.AddTaskAsync(p1.Id, "Due today", due: _today);
        await db.AddTaskAsync(p1.Id, "No due", TaskItemStatus.Blocked);
        await db.AddTaskAsync(p1.Id, "Done overdue", TaskItemStatus.Done, due: _today.AddDays(-3));
    }

    [Fact]
    public async Task Every_kpi_equals_its_click_through_list()
    {
        await SeedAsync();

        var kpis = await _dashboard.GetKpisAsync();

        var customers = await new CustomerService(db).ListAsync(new CustomerListQuery { Status = CustomerStatus.Active });
        var projectList = await new ProjectService(db, _user, db.Clock).ListAsync(new ProjectListQuery { PageSize = 100 });
        var openTasks = await _tasks.ListAsync(new TaskListQuery { Statuses = TaskListQuery.OpenStatuses });
        var overdue = await _tasks.ListAsync(new TaskListQuery { Statuses = TaskListQuery.OpenStatuses, OverdueOnly = true });

        Assert.Equal(customers.TotalCount, kpis.ActiveCustomers);
        Assert.Equal(2, kpis.ActiveCustomers);
        Assert.Equal(projectList.TotalCount, kpis.ActiveProjects);
        Assert.Equal(2, kpis.ActiveProjects);
        Assert.Equal(openTasks.TotalCount, kpis.OpenTasks);
        Assert.Equal(3, kpis.OpenTasks);
        Assert.Equal(overdue.TotalCount, kpis.OverdueTasks);
        Assert.Equal(1, kpis.OverdueTasks);
        Assert.Equal(projectList.Items.Sum(p => p.EstimatedValue ?? 0), kpis.PipelineValue);
        Assert.Equal(1000, kpis.PipelineValue);
    }

    [Fact]
    public async Task Status_bars_match_the_status_filtered_list_including_closed_statuses()
    {
        await SeedAsync();
        var projects = new ProjectService(db, _user, db.Clock);

        var bars = await _dashboard.GetProjectsByStatusAsync();

        Assert.Equal(8, bars.Count); // every status, including zero counts
        foreach (var bar in bars)
        {
            var list = await projects.ListAsync(new ProjectListQuery { StatusIds = [bar.StatusId], ActiveOnly = false });
            Assert.Equal(list.TotalCount, bar.ProjectCount);
        }

        Assert.Equal(1, bars.Single(b => b.Name == "Delivered").ProjectCount);
    }

    [Fact]
    public async Task Deadline_window_boundaries()
    {
        var customer = await db.AddCustomerAsync("C");
        var project = await db.AddProjectAsync(customer.Id, "P");
        await db.AddTaskAsync(project.Id, "Yesterday", due: _today.AddDays(-1));
        await db.AddTaskAsync(project.Id, "Today", due: _today);
        await db.AddTaskAsync(project.Id, "Plus14", due: _today.AddDays(14));
        await db.AddTaskAsync(project.Id, "Plus15", due: _today.AddDays(15));
        await db.AddTaskAsync(project.Id, "Done yesterday", TaskItemStatus.Done, due: _today.AddDays(-1));

        var deadlines = await _dashboard.GetUpcomingDeadlinesAsync();

        Assert.Equal(["Yesterday", "Today", "Plus14"], deadlines.Where(d => d.Kind == DeadlineKind.Task).Select(d => d.Title));
        Assert.True(deadlines[0].IsOverdue);
        Assert.Equal(0, deadlines[1].DaysFromToday);
        Assert.Equal(14, deadlines[2].DaysFromToday);
    }

    [Fact]
    public async Task Project_deadlines_skip_closed_statuses()
    {
        var customer = await db.AddCustomerAsync("C");
        var projects = new ProjectService(db, _user, db.Clock);
        await projects.CreateAsync(new ProjectInput { Name = "Open soon", CustomerId = customer.Id, TargetDate = _today.AddDays(3) });
        await projects.CreateAsync(new ProjectInput
        {
            Name = "Cancelled soon",
            CustomerId = customer.Id,
            TargetDate = _today.AddDays(3),
            StatusId = await db.StatusIdAsync("Cancelled"),
        });

        var deadlines = await _dashboard.GetUpcomingDeadlinesAsync();

        Assert.Equal(["Open soon"], deadlines.Select(d => d.Title));
    }

    [Fact]
    public async Task Future_activities_are_not_recent_and_my_tasks_are_mine_and_open()
    {
        var customer = await db.AddCustomerAsync("C");
        var project = await db.AddProjectAsync(customer.Id, "P");
        var activities = new ActivityService(db, _user, db.Clock);
        await activities.CreateAsync(new ActivityInput { Description = "Past", CustomerId = customer.Id, Date = DateTime.UtcNow.AddHours(-1) });
        await activities.CreateAsync(new ActivityInput { Description = "Future", CustomerId = customer.Id, Date = DateTime.UtcNow.AddDays(3) });
        var other = await db.AddUserAsync("other@test.local");
        await db.AddTaskAsync(project.Id, "Mine no due", assigneeId: _user.UserId);
        await db.AddTaskAsync(project.Id, "Mine due", assigneeId: _user.UserId, due: _today.AddDays(2));
        await db.AddTaskAsync(project.Id, "Mine done", TaskItemStatus.Done, assigneeId: _user.UserId);
        await db.AddTaskAsync(project.Id, "Theirs", assigneeId: other.Id);

        var recent = await _dashboard.GetRecentActivitiesAsync();
        var mine = await _dashboard.GetMyOpenTasksAsync();

        Assert.Equal(["Past"], recent.Select(a => a.Description));
        Assert.Equal(["Mine due", "Mine no due"], mine.Select(t => t.Title)); // due ascending, no due date last
    }

    [Fact]
    public async Task Empty_database_gives_zeros()
    {
        var all = await _dashboard.GetAsync();

        Assert.Equal(new DashboardKpis(0, 0, 0, 0, 0), all.Kpis);
        Assert.Empty(all.UpcomingDeadlines);
        Assert.Empty(all.MyOpenTasks);
        Assert.Empty(all.RecentActivities);
        Assert.Empty(all.RecentlyUpdated.Customers);
        Assert.All(all.ProjectsByStatus, b => Assert.Equal(0, b.ProjectCount));
    }
}
