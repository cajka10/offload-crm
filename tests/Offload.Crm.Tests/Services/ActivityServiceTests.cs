using Offload.Crm.Application.Activities;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Customers;
using Offload.Crm.Domain.Enums;
using Offload.Crm.Tests.Infrastructure;

namespace Offload.Crm.Tests.Services;

[Collection(DatabaseCollection.Name)]
public sealed class ActivityServiceTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new();
    private ActivityService _service = null!;
    private int _customerId;
    private int _projectId;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _user.UserId = (await db.AddUserAsync("author@test.local")).Id;
        _service = new ActivityService(db, _user, db.Clock);
        _customerId = (await db.AddCustomerAsync("C")).Id;
        _projectId = (await db.AddProjectAsync(_customerId, "P")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Customer_or_project_is_required()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.CreateAsync(new ActivityInput { Description = "No target" }));

        Assert.Equal([ActivityService.NoTargetMessage], ex.Errors[nameof(ActivityInput.CustomerId)]);
    }

    [Fact]
    public async Task Project_implies_its_customer()
    {
        var created = await _service.CreateAsync(new ActivityInput { Description = "Call", Type = ActivityType.Call, ProjectId = _projectId });

        Assert.Equal(_customerId, created.CustomerId);
        Assert.Equal(_user.UserId, created.AuthorId);
    }

    [Fact]
    public async Task Project_of_a_different_customer_is_rejected()
    {
        var other = await db.AddCustomerAsync("Other");

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.CreateAsync(new ActivityInput { Description = "X", CustomerId = other.Id, ProjectId = _projectId }));

        Assert.Equal([ActivityService.MismatchMessage], ex.Errors[nameof(ActivityInput.ProjectId)]);
    }

    [Fact]
    public async Task Date_more_than_a_year_ahead_is_rejected_and_defaults_to_now()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => _service.CreateAsync(new ActivityInput
        {
            Description = "Future",
            CustomerId = _customerId,
            Date = DateTime.UtcNow.AddYears(1).AddDays(2),
        }));
        Assert.Equal([ActivityService.FutureMessage], ex.Errors[nameof(ActivityInput.Date)]);

        var created = await _service.CreateAsync(new ActivityInput { Description = "Now", CustomerId = _customerId, Date = null });
        Assert.InRange(created.Date, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Undefined_type_and_missing_description_are_rejected()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.CreateAsync(new ActivityInput { Description = " ", CustomerId = _customerId, Type = (ActivityType)77 }));

        Assert.Contains(nameof(ActivityInput.Description), ex.Errors.Keys);
        Assert.Contains(nameof(ActivityInput.Type), ex.Errors.Keys);
        await Assert.ThrowsAsync<AppValidationException>(() => _service.ListAsync(new ActivityListQuery { Type = (ActivityType)77 }));
    }

    [Fact]
    public async Task Author_never_changes_on_update()
    {
        var created = await _service.CreateAsync(new ActivityInput { Description = "Mine", CustomerId = _customerId });
        _user.UserId = (await db.AddUserAsync("someone@test.local")).Id;

        var updated = await _service.UpdateAsync(created.Id, new ActivityInput { Description = "Edited", CustomerId = _customerId, Date = created.Date });

        Assert.Equal(created.AuthorId, updated.AuthorId);
        Assert.Equal("Edited", updated.Description);
    }

    [Fact]
    public async Task Create_without_signed_in_user_fails()
    {
        _user.UserId = null;

        await Assert.ThrowsAsync<SessionExpiredException>(() =>
            _service.CreateAsync(new ActivityInput { Description = "X", CustomerId = _customerId }));
    }

    [Fact]
    public async Task Customer_feed_includes_project_activities_newest_first_and_updates_last_activity()
    {
        var older = await _service.CreateAsync(new ActivityInput { Description = "Older", CustomerId = _customerId, Date = DateTime.UtcNow.AddDays(-2) });
        var newer = await _service.CreateAsync(new ActivityInput { Description = "Newer", ProjectId = _projectId, Date = DateTime.UtcNow.AddHours(-1) });
        var otherCustomer = await db.AddCustomerAsync("Other");
        await _service.CreateAsync(new ActivityInput { Description = "Elsewhere", CustomerId = otherCustomer.Id });

        var feed = await _service.ListAsync(new ActivityListQuery { CustomerId = _customerId });
        var customer = await new CustomerService(db).GetAsync(_customerId);

        Assert.Equal([newer.Id, older.Id], feed.Items.Select(a => a.Id));
        Assert.Equal(newer.Date, customer.LastActivityAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Date_range_filter_uses_local_days()
    {
        var day = new DateOnly(2026, 3, 10);
        await _service.CreateAsync(new ActivityInput { Description = "In", CustomerId = _customerId, Date = AppTime.StartOfDayUtc(day).AddHours(23) });
        await _service.CreateAsync(new ActivityInput { Description = "Out", CustomerId = _customerId, Date = AppTime.StartOfDayUtc(day.AddDays(1)) });

        var result = await _service.ListAsync(new ActivityListQuery { From = day, To = day });

        Assert.Equal(["In"], result.Items.Select(a => a.Description));
    }
}
