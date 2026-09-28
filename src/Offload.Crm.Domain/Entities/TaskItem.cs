using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Domain.Entities;

/// <summary>A project task. Named TaskItem to avoid clashing with <see cref="System.Threading.Tasks.Task"/>.</summary>
public class TaskItem : EntityBase
{
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    public TaskItemStatus Status { get; set; } = TaskItemStatus.Todo;

    public Priority Priority { get; set; } = Priority.Medium;

    public string? AssignedUserId { get; set; }

    public ApplicationUser? AssignedUser { get; set; }

    public DateOnly? DueDate { get; set; }

    /// <summary>UTC. Maintained by <see cref="ChangeStatus"/> (D-13).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Changes the status and keeps <see cref="CompletedAt"/> consistent (D-13).</summary>
    public void ChangeStatus(TaskItemStatus status, DateTime utcNow)
    {
        if (status == TaskItemStatus.Done)
        {
            CompletedAt ??= utcNow;
        }
        else
        {
            CompletedAt = null;
        }

        Status = status;
    }
}
