using Microsoft.AspNetCore.Identity;

namespace Offload.Crm.Domain.Entities;

/// <summary>Identity user (D-09). Keeps the Identity string key; users are never deleted, only deactivated.</summary>
public class ApplicationUser : IdentityUser, IAuditable
{
    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>UTC time of the last successful sign-in (D-24).</summary>
    public DateTime? LastSignInAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
