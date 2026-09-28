namespace Offload.Crm.Domain.Entities;

/// <summary>Entities with timestamps maintained automatically by the persistence layer (D-13).</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }

    DateTime UpdatedAt { get; set; }
}

public abstract class EntityBase : IAuditable
{
    public int Id { get; set; }

    /// <summary>UTC. Set by the DbContext interceptor.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC. Set by the DbContext interceptor.</summary>
    public DateTime UpdatedAt { get; set; }
}
