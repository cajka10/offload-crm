using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Tasks;
using Offload.Crm.Domain.Enums;
using Offload.Crm.Tests.Infrastructure;

namespace Offload.Crm.Tests.Services;

[Collection(DatabaseCollection.Name)]
public sealed class TaskServiceTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new();
    private TaskService _service = null!;
    private int _projectId;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _user.UserId = (await db.AddUserAsync("me@test.local")).Id;
        _service = new TaskService(db, _user, db.Clock);
        var customer = await db.AddCustomerAsync("C");
        _projectId = (await db.AddProjectAsync(customer.Id, "P")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Completed_at_is_set_on_done_and_cleared_when_leaving_done()
    {
        var task = await _service.CreateAsync(new TaskInput { Title = "T", ProjectId = _projectId });
        Assert.Null(task.CompletedAt);

        var done = await _service.ChangeStatusAsync(task.Id, TaskItemStatus.Done);
        Assert.NotNull(done.CompletedAt);

        var reopened = await _service.ChangeStatusAsync(task.Id, TaskItemStatus.InProgress);
        Assert.Null(reopened.CompletedAt);

        var input = TaskInput.From(reopened);
        input.Status = TaskItemStatus.Done;
        var viaUpdate = await _service.UpdateAsync(task.Id, input);
        Assert.NotNull(viaUpdate.CompletedAt);
    }

    [Fact]
    public async Task Created_as_done_has_completed_at()
    {
        var task = await _service.CreateAsync(new TaskInput { Title = "Done already", ProjectId = _projectId, Status = TaskItemStatus.Done });

        Assert.NotNull(task.CompletedAt);
    }

    [Fact]
    public async Task Validation_rules()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => _service.CreateAsync(new TaskInput
        {
            Title = "  ",
            ProjectId = null,
            Status = (TaskItemStatus)9,
            Priority = (Priority)9,
        }));

        Assert.Contains(nameof(TaskInput.Title), ex.Errors.Keys);
        Assert.Contains(nameof(TaskInput.ProjectId), ex.Errors.Keys);
        Assert.Contains(nameof(TaskInput.Status), ex.Errors.Keys);
        Assert.Contains(nameof(TaskInput.Priority), ex.Errors.Keys);
        await Assert.ThrowsAsync<AppValidationException>(() => _service.ChangeStatusAsync(1, (TaskItemStatus)9));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ChangeStatusAsync(987654, TaskItemStatus.Done));
    }

    [Fact]
    public async Task Assignee_must_be_active_for_new_assignment()
    {
        var inactive = await db.AddUserAsync("gone@test.local");
        await db.SetUserActiveAsync(inactive.Id, false);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.CreateAsync(new TaskInput { Title = "T", ProjectId = _projectId, AssignedUserId = inactive.Id }));

        Assert.Contains(nameof(TaskInput.AssignedUserId), ex.Errors.Keys);
    }

    [Fact]
    public async Task Filters_me_unassigned_overdue_and_open()
    {
        var other = await db.AddUserAsync("other@test.local");
        var yesterday = AppTime.Today(db.Clock).AddDays(-1);
        var mine = await db.AddTaskAsync(_projectId, "Mine", assigneeId: _user.UserId, due: yesterday);
        var nobody = await db.AddTaskAsync(_projectId, "Nobody");
        await db.AddTaskAsync(_projectId, "Other", assigneeId: other.Id);
        var doneOverdue = await db.AddTaskAsync(_projectId, "Done", TaskItemStatus.Done, due: yesterday);

        var me = await _service.ListAsync(new TaskListQuery { Assignee = "me" });
        var unassigned = await _service.ListAsync(new TaskListQuery { Assignee = "unassigned" });
        var overdue = await _service.ListAsync(new TaskListQuery { OverdueOnly = true });
        var open = await _service.ListAsync(new TaskListQuery { Statuses = TaskListQuery.OpenStatuses });

        Assert.Equal([mine.Id], me.Items.Select(t => t.Id));
        Assert.Equal([nobody.Id, doneOverdue.Id], unassigned.Items.Select(t => t.Id).Order());
        Assert.Equal([mine.Id], overdue.Items.Select(t => t.Id));
        Assert.DoesNotContain(open.Items, t => t.Id == doneOverdue.Id);
        Assert.Equal(3, open.TotalCount);
    }

    [Fact]
    public async Task Undefined_status_filter_is_rejected()
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.ListAsync(new TaskListQuery { Statuses = [(TaskItemStatus)42] }));
    }

    [Fact]
    public async Task Due_sort_puts_tasks_without_due_date_last_and_status_sorts_by_lifecycle()
    {
        await db.AddTaskAsync(_projectId, "No due", TaskItemStatus.Done);
        await db.AddTaskAsync(_projectId, "Due", TaskItemStatus.Todo, due: new DateOnly(2026, 1, 1));

        var desc = await _service.ListAsync(new TaskListQuery { Sort = TaskSort.Due, Descending = true });
        var byStatus = await _service.ListAsync(new TaskListQuery { Sort = TaskSort.Status });

        Assert.Equal(["Due", "No due"], desc.Items.Select(t => t.Title));
        Assert.Equal(["Due", "No due"], byStatus.Items.Select(t => t.Title)); // Todo before Done
    }

    [Fact]
    public async Task Board_returns_all_matching_tasks_without_paging()
    {
        for (var i = 0; i < 30; i++)
        {
            await db.AddTaskAsync(_projectId, $"T{i}");
        }

        var board = await _service.GetBoardAsync(new TaskListQuery { ProjectId = _projectId }, recentDoneOnly: true);

        Assert.Equal(30, board.Tasks.Count);
        Assert.Equal(30, board.TotalCount);
        Assert.Equal(0, board.HiddenCount);
    }

    [Fact]
    public async Task Board_done_column_shows_only_the_last_30_days_unless_done_is_requested()
    {
        var recent = await db.AddTaskAsync(_projectId, "Recent done", TaskItemStatus.Done);
        var old = await db.AddTaskAsync(_projectId, "Old done", TaskItemStatus.Done);
        await using (var ctx = db.CreateContext())
        {
            var entity = await ctx.Tasks.SingleAsync(t => t.Id == old.Id);
            entity.CompletedAt = DateTime.UtcNow.AddDays(-31);
            await ctx.SaveChangesAsync();
        }

        var open = await db.AddTaskAsync(_projectId, "Open");

        var window = await _service.GetBoardAsync(new TaskListQuery(), recentDoneOnly: true);
        var explicitDone = await _service.GetBoardAsync(new TaskListQuery { Statuses = [TaskItemStatus.Done] }, recentDoneOnly: false);

        Assert.Equal([recent.Id, open.Id], window.Tasks.Select(t => t.Id).Order());
        Assert.Equal(1, window.OlderDoneCount);
        Assert.Equal([recent.Id, old.Id], explicitDone.Tasks.Select(t => t.Id).Order());
        Assert.Equal(0, explicitDone.OlderDoneCount);
    }
}
