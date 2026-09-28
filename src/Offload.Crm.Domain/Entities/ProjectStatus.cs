namespace Offload.Crm.Domain.Entities;

/// <summary>Configurable project status (D-02).</summary>
public class ProjectStatus : EntityBase
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex color, e.g. <c>#0F5132</c>.</summary>
    public string Color { get; set; } = "#6C757D";

    public int SortOrder { get; set; }

    /// <summary>Projects in this status count as active (D-03).</summary>
    public bool IsActive { get; set; }

    /// <summary>Terminal status (e.g. Delivered, Cancelled).</summary>
    public bool IsClosed { get; set; }

    /// <summary>Exactly one status is the default for new projects.</summary>
    public bool IsDefault { get; set; }

    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
