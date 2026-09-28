using System.ComponentModel.DataAnnotations;
using Offload.Crm.Application.Common;

namespace Offload.Crm.Application.Contacts;

public sealed record ContactDto(
    int Id,
    int CustomerId,
    string? FirstName,
    string LastName,
    string FullName,
    string? Position,
    string? Email,
    string? Phone,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Create/update payload for a contact (D-12). The customer comes from the route / dialog context.</summary>
public sealed class ContactInput : INormalizable
{
    [StringLength(100, ErrorMessage = "First name can be at most 100 characters.")]
    public string? FirstName { get; set; }

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(100, ErrorMessage = "Last name can be at most 100 characters.")]
    public string? LastName { get; set; }

    [StringLength(100, ErrorMessage = "Position can be at most 100 characters.")]
    public string? Position { get; set; }

    [StringLength(254, ErrorMessage = "E-mail can be at most 254 characters.")]
    [EmailFormat]
    public string? Email { get; set; }

    [StringLength(30, ErrorMessage = "Phone can be at most 30 characters.")]
    [PhoneChars]
    public string? Phone { get; set; }

    [StringLength(4000, ErrorMessage = "Notes can be at most 4000 characters.")]
    public string? Notes { get; set; }

    public void Normalize()
    {
        FirstName = InputText.Clean(FirstName);
        LastName = InputText.Clean(LastName);
        Position = InputText.Clean(Position);
        Email = InputText.Clean(Email);
        Phone = InputText.Clean(Phone);
        Notes = InputText.Clean(Notes);
    }

    public static ContactInput From(ContactDto c) => new()
    {
        FirstName = c.FirstName,
        LastName = c.LastName,
        Position = c.Position,
        Email = c.Email,
        Phone = c.Phone,
        Notes = c.Notes,
    };
}

public sealed class ContactListQuery : PagedQuery
{
    public int? CustomerId { get; set; }
}
