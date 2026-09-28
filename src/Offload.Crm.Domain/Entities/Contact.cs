namespace Offload.Crm.Domain.Entities;

public class Contact : EntityBase
{
    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public string? FirstName { get; set; }

    public string LastName { get; set; } = string.Empty;

    public string? Position { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Notes { get; set; }
}
