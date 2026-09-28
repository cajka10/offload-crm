using System.ComponentModel.DataAnnotations;
using Offload.Crm.Application.Common;

namespace Offload.Crm.Application.Users;

/// <summary>A user in Settings → Users (D-24). Never contains password hashes or security stamps.</summary>
public sealed record UserListItem(
    string Id,
    string FullName,
    string? Email,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastSignInAt,
    bool IsCurrentUser,
    bool IsAdmin = false);

public sealed class UserListQuery
{
    /// <summary>Name or e-mail contains (case-insensitive).</summary>
    public string? Search { get; set; }

    /// <summary>Null = all users.</summary>
    public bool? Active { get; set; }
}

/// <summary>What deactivating a user leaves behind (shown in the confirmation, D-24).</summary>
public sealed record DeactivationImpact(int OpenTasks, int ActiveProjects);

public sealed class UserCreateInput : INormalizable
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(100, ErrorMessage = "Full name can be at most 100 characters.")]
    public string? FullName { get; set; }

    [Required(ErrorMessage = "E-mail is required.")]
    [StringLength(256, ErrorMessage = "E-mail can be at most 256 characters.")]
    [EmailFormat]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Password is required.")]
    public string? Password { get; set; }

    /// <summary>D-27: member of the Admin role.</summary>
    public bool IsAdmin { get; set; }

    [Required(ErrorMessage = "Confirm the password.")]
    [Compare(nameof(Password), ErrorMessage = "The passwords don't match.")]
    public string? ConfirmPassword { get; set; }

    public void Normalize()
    {
        FullName = InputText.Clean(FullName);
        Email = InputText.Clean(Email);
    }
}

public sealed class UserUpdateInput : INormalizable
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(100, ErrorMessage = "Full name can be at most 100 characters.")]
    public string? FullName { get; set; }

    [Required(ErrorMessage = "E-mail is required.")]
    [StringLength(256, ErrorMessage = "E-mail can be at most 256 characters.")]
    [EmailFormat]
    public string? Email { get; set; }

    /// <summary>D-27: member of the Admin role. You can.t remove it from yourself or from the last active administrator.</summary>
    public bool IsAdmin { get; set; }

    public void Normalize()
    {
        FullName = InputText.Clean(FullName);
        Email = InputText.Clean(Email);
    }
}

/// <summary>Admin reset of another user's password.</summary>
public sealed class PasswordResetInput
{
    [Required(ErrorMessage = "Password is required.")]
    public string? Password { get; set; }

    [Required(ErrorMessage = "Confirm the password.")]
    [Compare(nameof(Password), ErrorMessage = "The passwords don't match.")]
    public string? ConfirmPassword { get; set; }
}

public sealed class ChangeOwnPasswordInput
{
    [Required(ErrorMessage = "Enter your current password.")]
    public string? CurrentPassword { get; set; }

    [Required(ErrorMessage = "Enter a new password.")]
    public string? NewPassword { get; set; }

    [Required(ErrorMessage = "Confirm the new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords don't match.")]
    public string? ConfirmPassword { get; set; }
}

/// <summary>
/// User management (D-24). Every method except <see cref="ChangeOwnPasswordAsync"/> requires the <c>Admin</c> policy (D-27) and throws <see cref="ForbiddenException"/> otherwise. Identity errors become field errors.
/// </summary>
public interface IUserAdminService
{
    /// <summary>Active users first, then by name. Not paged (small user base).</summary>
    Task<IReadOnlyList<UserListItem>> ListAsync(UserListQuery query, CancellationToken ct = default);

    Task<UserListItem> GetAsync(string id, CancellationToken ct = default);

    /// <summary>Creates an active user with a confirmed e-mail. No e-mail is sent.</summary>
    Task<UserListItem> CreateAsync(UserCreateInput input, CancellationToken ct = default);

    /// <summary>Changes name and e-mail (the e-mail is also the login user name).</summary>
    Task<UserListItem> UpdateAsync(string id, UserUpdateInput input, CancellationToken ct = default);

    Task<DeactivationImpact> GetDeactivationImpactAsync(string id, CancellationToken ct = default);

    /// <summary>Deactivates and ends all sessions. 409 for yourself or the last active user.</summary>
    Task DeactivateAsync(string id, CancellationToken ct = default);

    /// <summary>Reactivates and clears any lockout.</summary>
    Task ReactivateAsync(string id, CancellationToken ct = default);

    /// <summary>Sets a new password, ends all of that user's sessions and clears any lockout.</summary>
    Task ResetPasswordAsync(string id, PasswordResetInput input, CancellationToken ct = default);

    /// <summary>
    /// Changes the current user's password (ends their other sessions). Returns a one-time sign-in ticket that
    /// the account page exchanges for a fresh cookie so the current session stays signed in.
    /// </summary>
    Task<string> ChangeOwnPasswordAsync(ChangeOwnPasswordInput input, CancellationToken ct = default);
}
