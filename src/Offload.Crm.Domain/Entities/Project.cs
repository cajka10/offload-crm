using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Domain.Entities;

public class Project : EntityBase
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public int StatusId { get; set; }

    public ProjectStatus Status { get; set; } = null!;

    public Priority Priority { get; set; } = Priority.Medium;

    public string? ResponsibleUserId { get; set; }

    public ApplicationUser? ResponsibleUser { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? TargetDate { get; set; }

    /// <summary>EUR (D-28).</summary>
    public decimal? EstimatedValue { get; set; }

    /// <summary>EUR (D-28).</summary>
    public decimal? ActualValue { get; set; }

    public string? Notes { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();

    public ICollection<Activity> Activities { get; set; } = new List<Activity>();
}
