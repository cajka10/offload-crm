using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Domain.Entities;

/// <summary>A logged interaction (call, e-mail, meeting, note). At least one of Customer/Project is set (D-12).</summary>
public class Activity : EntityBase
{
    public ActivityType Type { get; set; } = ActivityType.Note;

    /// <summary>UTC.</summary>
    public DateTime Date { get; set; }

    /// <summary>Optional short headline shown in lists and timelines.</summary>
    public string? Subject { get; set; }

    public string Description { get; set; } = string.Empty;

    public int? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    public string AuthorId { get; set; } = string.Empty;

    public ApplicationUser Author { get; set; } = null!;
}
