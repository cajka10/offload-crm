using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Projects;
using Offload.Crm.Domain.Enums;
using Offload.Crm.Tests.Infrastructure;

namespace Offload.Crm.Tests.Services;

[Collection(DatabaseCollection.Name)]
public sealed class ProjectServiceTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new();
    private ProjectService _service = null!;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _user.UserId = (await db.AddUserAsync()).Id;
        _service = new ProjectService(db, _user, db.Clock);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<ProjectDetail> CreateAsync(string name = "Web", int? customerId = null)
    {
        customerId ??= (await db.AddCustomerAsync("Customer " + Guid.NewGuid().ToString("N")[..6])).Id;
        return await _service.CreateAsync(new ProjectInput { Name = name, CustomerId = customerId });
    }

    [Fact]
    public async Task Create_uses_the_default_status_and_medium_priority()
    {
        var created = await CreateAsync();

        Assert.Equal("Lead", created.StatusName);
        Assert.Equal(Priority.Medium, created.Priority);
    }

    [Fact]
    public async Task Target_before_start_is_rejected()
    {
        var customer = await db.AddCustomerAsync("C");

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => _service.CreateAsync(new ProjectInput
        {
            Name = "P",
            CustomerId = customer.Id,
            StartDate = new DateOnly(2026, 10, 10),
            TargetDate = new DateOnly(2026, 10, 9),
        }));

        Assert.Contains(nameof(ProjectInput.TargetDate), ex.Errors.Keys);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_000_000_000)]
    [InlineData(10.555)]
    public async Task Invalid_values_are_rejected(double value)
    {
        var customer = await db.AddCustomerAsync("C");

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => _service.CreateAsync(new ProjectInput
        {
            Name = "P",
            CustomerId = customer.Id,
            EstimatedValue = (decimal)value,
            ActualValue = (decimal)value,
        }));

        Assert.Contains(nameof(ProjectInput.EstimatedValue), ex.Errors.Keys);
        Assert.Contains(nameof(ProjectInput.ActualValue), ex.Errors.Keys);
    }

    [Fact]
    public async Task Valid_boundaries_are_accepted()
    {
        var customer = await db.AddCustomerAsync("C");

        var created = await _service.CreateAsync(new ProjectInput
        {
            Name = "P",
            CustomerId = customer.Id,
            EstimatedValue = 999_999_999_999m,
            ActualValue = 0.01m,
            StartDate = new DateOnly(2026, 1, 1),
            TargetDate = new DateOnly(2026, 1, 1),
        });

        Assert.Equal(999_999_999_999m, created.EstimatedValue);
    }

    [Fact]
    public async Task Missing_or_unknown_customer_and_status_are_rejected()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.CreateAsync(new ProjectInput { Name = "P", CustomerId = null, StatusId = 99999 }));

        Assert.Contains(nameof(ProjectInput.CustomerId), ex.Errors.Keys);
        Assert.Contains(nameof(ProjectInput.StatusId), ex.Errors.Keys);

        var unknown = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.CreateAsync(new ProjectInput { Name = "P", CustomerId = 99999 }));
        Assert.Contains(nameof(ProjectInput.CustomerId), unknown.Errors.Keys);
    }

    [Fact]
    public async Task Status_change_logs_a_note_activity()
    {
        var project = await CreateAsync();
        var development = await db.StatusIdAsync("Development");

        var changed = await _service.ChangeStatusAsync(project.Id, development);

        Assert.Equal("Development", changed.StatusName);
        await using var ctx = db.CreateContext();
        var activity = await ctx.Activities.SingleAsync(a => a.ProjectId == project.Id);
        Assert.Equal(ActivityType.Note, activity.Type);
        Assert.Equal("Status changed", activity.Subject);
        Assert.Equal("Status changed: Lead → Development", activity.Description);
        Assert.Equal(_user.UserId, activity.AuthorId);
        Assert.Equal(project.CustomerId, activity.CustomerId);

        var timeline = await _service.GetTimelineAsync(project.Id);
        Assert.Equal(TimelineKind.Activity, timeline[0].Kind);
        Assert.Equal(TimelineKind.ProjectCreated, timeline[^1].Kind);
    }

    [Fact]
    public async Task Update_with_new_status_logs_activity_but_same_status_does_not()
    {
        var project = await CreateAsync();
        var input = ProjectInput.From(project);

        await _service.UpdateAsync(project.Id, input); // unchanged status
        input.StatusId = await db.StatusIdAsync("Testing");
        await _service.UpdateAsync(project.Id, input);

        await using var ctx = db.CreateContext();
        var descriptions = await ctx.Activities.Where(a => a.ProjectId == project.Id).Select(a => a.Description).ToListAsync();
        Assert.Equal(["Status changed: Lead → Testing"], descriptions);
    }

    [Fact]
    public async Task Status_change_without_signed_in_user_fails()
    {
        var project = await CreateAsync();
        _user.UserId = null;

        await Assert.ThrowsAsync<SessionExpiredException>(() => _service.ChangeStatusAsync(project.Id, project.StatusId + 1));
    }

    [Fact]
    public async Task Delete_cascades_tasks_and_keeps_activities_with_customer()
    {
        var project = await CreateAsync();
        await db.AddTaskAsync(project.Id, "T1");
        await db.AddTaskAsync(project.Id, "T2");
        await db.AddActivityAsync(_user.UserId!, DateTime.UtcNow, customerId: project.CustomerId, projectId: project.Id);

        await _service.DeleteAsync(project.Id);

        await using var ctx = db.CreateContext();
        Assert.False(await ctx.Tasks.AnyAsync());
        var activity = await ctx.Activities.SingleAsync();
        Assert.Null(activity.ProjectId);
        Assert.Equal(project.CustomerId, activity.CustomerId);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(project.Id));
    }

    [Fact]
    public async Task Changing_the_customer_moves_the_project_activities()
    {
        var project = await CreateAsync();
        await db.AddActivityAsync(_user.UserId!, DateTime.UtcNow, customerId: project.CustomerId, projectId: project.Id);
        var other = await db.AddCustomerAsync("Other");
        var input = ProjectInput.From(project);
        input.CustomerId = other.Id;

        await _service.UpdateAsync(project.Id, input);

        await using var ctx = db.CreateContext();
        var activities = await ctx.Activities.Where(a => a.ProjectId == project.Id).ToListAsync();
        Assert.All(activities, a => Assert.Equal(other.Id, a.CustomerId));
        var note = Assert.Single(activities, a => a.Subject == "Customer changed");
        Assert.Equal(ActivityType.Note, note.Type);
        Assert.Equal($"Customer changed: {project.CustomerName} → Other", note.Description);
        Assert.Equal(2, activities.Count);
    }

    [Fact]
    public async Task Inactive_responsible_is_rejected_for_new_assignments_but_kept_on_edit()
    {
        var customer = await db.AddCustomerAsync("C");
        var jana = await db.AddUserAsync("jana@test.local");
        var project = await _service.CreateAsync(new ProjectInput { Name = "P", CustomerId = customer.Id, ResponsibleUserId = jana.Id });
        await db.SetUserActiveAsync(jana.Id, false);

        var edited = await _service.UpdateAsync(project.Id, ProjectInput.From(project));
        Assert.Equal(jana.Id, edited.ResponsibleUserId);
        Assert.False(edited.ResponsibleIsActive);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.CreateAsync(new ProjectInput { Name = "P2", CustomerId = customer.Id, ResponsibleUserId = jana.Id }));
        Assert.Contains(nameof(ProjectInput.ResponsibleUserId), ex.Errors.Keys);
    }

    [Fact]
    public async Task List_filters_active_only_by_default_and_searches_customer_name()
    {
        var customer = await db.AddCustomerAsync("Žluťoučký kůň");
        var open = await _service.CreateAsync(new ProjectInput { Name = "Open", CustomerId = customer.Id });
        var closed = await _service.CreateAsync(new ProjectInput { Name = "Closed", CustomerId = customer.Id, StatusId = await db.StatusIdAsync("Delivered") });

        var active = await _service.ListAsync(new ProjectListQuery());
        var all = await _service.ListAsync(new ProjectListQuery { ActiveOnly = false, Search = "ŽLUŤOUČKÝ" });
        var byStatus = await _service.ListAsync(new ProjectListQuery { ActiveOnly = false, StatusIds = [closed.StatusId] });

        Assert.Equal([open.Id], active.Items.Select(p => p.Id));
        Assert.Equal(2, all.TotalCount);
        Assert.Equal([closed.Id], byStatus.Items.Select(p => p.Id));
    }

    [Theory]
    [InlineData(ProjectSort.Name)]
    [InlineData(ProjectSort.Customer)]
    [InlineData(ProjectSort.Status)]
    [InlineData(ProjectSort.Priority)]
    [InlineData(ProjectSort.Responsible)]
    [InlineData(ProjectSort.Start)]
    [InlineData(ProjectSort.Target)]
    [InlineData(ProjectSort.EstimatedValue)]
    [InlineData(ProjectSort.OpenTasks)]
    [InlineData(ProjectSort.Created)]
    public async Task Every_sort_column_works_and_nulls_are_last(string sort)
    {
        var customer = await db.AddCustomerAsync("C");
        var jana = await db.AddUserAsync("jana@test.local");
        var full = await _service.CreateAsync(new ProjectInput
        {
            Name = "Full",
            CustomerId = customer.Id,
            ResponsibleUserId = jana.Id,
            StartDate = new DateOnly(2026, 1, 1),
            TargetDate = new DateOnly(2026, 2, 1),
            EstimatedValue = 10,
        });
        await _service.CreateAsync(new ProjectInput { Name = "Empty", CustomerId = customer.Id });

        var desc = await _service.ListAsync(new ProjectListQuery { Sort = sort, Descending = true });
        var asc = await _service.ListAsync(new ProjectListQuery { Sort = sort });

        Assert.Equal(2, desc.Items.Count);
        if (sort is ProjectSort.Responsible or ProjectSort.Start or ProjectSort.Target or ProjectSort.EstimatedValue)
        {
            Assert.Equal(full.Id, desc.Items[0].Id);
            Assert.Equal(full.Id, asc.Items[0].Id);
        }
    }

    [Fact]
    public async Task Priority_sorts_in_declaration_order()
    {
        var customer = await db.AddCustomerAsync("C");
        foreach (var p in new[] { Priority.High, Priority.Low, Priority.Critical, Priority.Medium })
        {
            await _service.CreateAsync(new ProjectInput { Name = p.ToString(), CustomerId = customer.Id, Priority = p });
        }

        var asc = await _service.ListAsync(new ProjectListQuery { Sort = ProjectSort.Priority });

        Assert.Equal(["Low", "Medium", "High", "Critical"], asc.Items.Select(p => p.Name));
    }
}
