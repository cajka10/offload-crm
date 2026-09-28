using Offload.Crm.Domain.Entities;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Tests.Unit;

/// <summary>D-13: CompletedAt is set when a task becomes Done and cleared when it leaves Done.</summary>
public class TaskItemTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Done_sets_completed_at()
    {
        var task = new TaskItem();

        task.ChangeStatus(TaskItemStatus.Done, Now);

        Assert.Equal(TaskItemStatus.Done, task.Status);
        Assert.Equal(Now, task.CompletedAt);
    }

    [Fact]
    public void Leaving_done_clears_completed_at()
    {
        var task = new TaskItem();
        task.ChangeStatus(TaskItemStatus.Done, Now);

        task.ChangeStatus(TaskItemStatus.InProgress, Now.AddHours(1));

        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void Done_again_keeps_original_completion_time()
    {
        var task = new TaskItem();
        task.ChangeStatus(TaskItemStatus.Done, Now);

        task.ChangeStatus(TaskItemStatus.Done, Now.AddHours(1));

        Assert.Equal(Now, task.CompletedAt);
    }
}
