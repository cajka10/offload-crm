using Offload.Crm.Application.Activities;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Projects;
using Offload.Crm.Application.ProjectStatuses;
using Offload.Crm.Application.Tasks;
using Offload.Crm.Domain.Enums;
using Offload.Crm.Tests.Infrastructure;

namespace Offload.Crm.Tests.Services;

/// <summary>Service-level regression tests for the T-22 bugs (BUG-017, 020, 026, 027).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class BugRegressionTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new();
    private ProjectService _projects = null!;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _user.UserId = (await db.AddUserAsync()).Id;
        _projects = new ProjectService(db, _user, db.Clock);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Bug017_explicit_statuses_override_active_only()
    {
        var customer = await db.AddCustomerAsync("C");
        var delivered = await db.StatusIdAsync("Delivered");
        var closed = await _projects.CreateAsync(new ProjectInput { Name = "Closed", CustomerId = customer.Id, StatusId = delivered });
        var open = await _projects.CreateAsync(new ProjectInput { Name = "Open", CustomerId = customer.Id });

        var byClosed = await _projects.ListAsync(new ProjectListQuery { StatusIds = [delivered], ActiveOnly = true });
        var both = await _projects.ListAsync(new ProjectListQuery { StatusIds = [delivered, open.StatusId], ActiveOnly = true });
        var defaultList = await _projects.ListAsync(new ProjectListQuery());

        Assert.Equal([closed.Id], byClosed.Items.Select(p => p.Id));
        Assert.Equal(2, both.TotalCount);
        Assert.Equal([open.Id], defaultList.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task Bug020_closed_status_cant_be_or_become_the_default()
    {
        var statuses = new ProjectStatusService(db);
        var cancelled = await db.StatusIdAsync("Cancelled");
        var lead = await db.StatusIdAsync("Lead");

        var setDefault = await Assert.ThrowsAsync<ConflictException>(() => statuses.SetDefaultAsync(cancelled));
        Assert.Equal(ProjectStatusService.ClosedDefaultMessage, setDefault.Message);

        var closeDefault = await Assert.ThrowsAsync<AppValidationException>(() =>
            statuses.UpdateAsync(lead, new ProjectStatusInput { Name = "Lead", Color = "#6C757D", IsActive = false, IsClosed = true, IsDefault = true }));
        Assert.Equal([ProjectStatusService.ClosedDefaultMessage], closeDefault.Errors[nameof(ProjectStatusInput.IsClosed)]);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            statuses.CreateAsync(new ProjectStatusInput { Name = "Closed default", Color = "#000000", IsActive = false, IsClosed = true, IsDefault = true }));
        Assert.Equal("Lead", Assert.Single(await statuses.ListAsync(), s => s.IsDefault).Name);
    }

    [Theory]
    [InlineData("0226-01-01")]
    [InlineData("0001-01-01")]
    [InlineData("9999-12-31")]
    public async Task Bug026_implausible_dates_are_rejected(string date)
    {
        var customer = await db.AddCustomerAsync("C");
        var value = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

        var project = await Assert.ThrowsAsync<AppValidationException>(() =>
            _projects.CreateAsync(new ProjectInput { Name = "P", CustomerId = customer.Id, StartDate = value, TargetDate = value }));
        Assert.Contains(nameof(ProjectInput.StartDate), project.Errors.Keys);
        Assert.Contains(nameof(ProjectInput.TargetDate), project.Errors.Keys);

        var projectId = (await db.AddProjectAsync(customer.Id)).Id;
        var task = await Assert.ThrowsAsync<AppValidationException>(() =>
            new TaskService(db, _user, db.Clock).CreateAsync(new TaskInput { Title = "T", ProjectId = projectId, DueDate = value }));
        Assert.Contains(nameof(TaskInput.DueDate), task.Errors.Keys);

        var activity = await Assert.ThrowsAsync<AppValidationException>(() =>
            new ActivityService(db, _user, db.Clock).CreateAsync(new ActivityInput
            {
                Description = "x",
                CustomerId = customer.Id,
                Date = DateTime.SpecifyKind(value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            }));
        Assert.Contains(nameof(ActivityInput.Date), activity.Errors.Keys);
    }

    [Fact]
    public async Task Bug026_activity_date_without_offset_is_prague_local_time()
    {
        var customer = await db.AddCustomerAsync("C");
        var service = new ActivityService(db, _user, db.Clock);

        var local = await service.CreateAsync(new ActivityInput
        {
            Description = "Local",
            CustomerId = customer.Id,
            Date = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Unspecified),
        });
        var utc = await service.CreateAsync(new ActivityInput
        {
            Description = "UTC",
            CustomerId = customer.Id,
            Date = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc),
        });

        Assert.Equal(new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc), local.Date); // CEST = UTC+2
        Assert.Equal(new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc), utc.Date);
    }

    [Fact]
    public async Task Bug027_timeline_uses_display_names()
    {
        var customer = await db.AddCustomerAsync("C");
        var project = await db.AddProjectAsync(customer.Id);
        await new ActivityService(db, _user, db.Clock).CreateAsync(new ActivityInput { Type = ActivityType.Call, Description = "x", ProjectId = project.Id });

        var timeline = await _projects.GetTimelineAsync(project.Id);

        Assert.Equal("Phone call", timeline.First(i => i.Kind == TimelineKind.Activity).Title);
    }
}
