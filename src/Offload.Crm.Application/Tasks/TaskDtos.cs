using System.ComponentModel.DataAnnotations;
using Offload.Crm.Application.Common;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Application.Tasks;

/// <summary>Sort keys accepted by <see cref="ITaskService.ListAsync"/> (case-insensitive).</summary>
public static class TaskSort
{
    public const string Title = "title";
    public const string Project = "project";
    public const string Customer = "customer";
    public const string Status = "status";
    public const string Priority = "priority";
    public const string Assignee = "assignee";
    public const string Due = "due";
    public const string Created = "created";
}

public sealed class TaskListQuery : PagedQuery
{
    /// <summary>Open statuses: the default filter of the Tasks page (D-03 "open task" = not Done).</summary>
    public static readonly IReadOnlyList<TaskItemStatus> OpenStatuses = [TaskItemStatus.Todo, TaskItemStatus.InProgress, TaskItemStatus.Blocked];

    /// <summary>"me" (current user), "unassigned", or a user id.</summary>
    public const string AssigneeMe = "me";
    public const string AssigneeUnassigned = "unassigned";

    /// <summary>Any of these statuses (null/empty = all).</summary>
    public IReadOnlyList<TaskItemStatus>? Statuses { get; set; }

    public Priority? Priority { get; set; }

    public string? Assignee { get; set; }

    public int? ProjectId { get; set; }

    public int? CustomerId { get; set; }

    /// <summary>Due date before today and not Done (D-03).</summary>
    public bool OverdueOnly { get; set; }
}

public sealed record TaskDto(
    int Id,
    string Title,
    string? Description,
    int ProjectId,
    string ProjectName,
    int CustomerId,
    string CustomerName,
    TaskItemStatus Status,
    Priority Priority,
    string? AssignedUserId,
    string? AssigneeName,
    bool AssigneeIsActive,
    DateOnly? DueDate,
    DateTime? CompletedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    /// <summary>D-03: due date before today and not Done.</summary>
    public bool IsOverdue(DateOnly today) => DueDate < today && Status != TaskItemStatus.Done;
}

/// <summary>Create/update payload (D-12).</summary>
public sealed class TaskInput : INormalizable
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, ErrorMessage = "Title can be at most 200 characters.")]
    public string? Title { get; set; }

    [StringLength(4000, ErrorMessage = "Description can be at most 4000 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Select a project.")]
    public int? ProjectId { get; set; }

    [EnumDefined(ErrorMessage = "Select a valid status.")]
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Todo;

    [EnumDefined(ErrorMessage = "Select a valid priority.")]
    public Priority Priority { get; set; } = Priority.Medium;

    public string? AssignedUserId { get; set; }

    [PlausibleDate]
    public DateOnly? DueDate { get; set; }

    public void Normalize()
    {
        Title = InputText.Clean(Title);
        Description = InputText.Clean(Description);
        AssignedUserId = InputText.Clean(AssignedUserId);
    }

    public static TaskInput From(TaskDto t) => new()
    {
        Title = t.Title,
        Description = t.Description,
        ProjectId = t.ProjectId,
        Status = t.Status,
        Priority = t.Priority,
        AssignedUserId = t.AssignedUserId,
        DueDate = t.DueDate,
    };
}

/// <summary>Body of <c>PATCH /api/tasks/{id}/status</c>. <c>status</c> is required (BUG-019).</summary>
public sealed record TaskStatusChange(TaskItemStatus? Status);

/// <summary>Kanban data: the cards, how many tasks matched in total (cap notice) and how many older Done tasks were left out.</summary>
public sealed record TaskBoard(IReadOnlyList<TaskDto> Tasks, int TotalCount, int OlderDoneCount)
{
    public int HiddenCount => Math.Max(0, TotalCount - Tasks.Count);
}
