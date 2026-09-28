using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Abstractions;

namespace Offload.Crm.Application.Users;

public sealed record UserDto(string Id, string FullName, string? Email, bool IsActive)
{
    /// <summary>Name for display; deactivated users are marked "(inactive)".</summary>
    public string DisplayName => IsActive ? FullName : $"{FullName} (inactive)";
}

/// <summary>Read-only user queries for pickers and filters (management comes in T-24).</summary>
public interface IUserService
{
    /// <summary>Users ordered by name; by default only active ones (pickers, D-09).</summary>
    Task<IReadOnlyList<UserDto>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
}

public sealed class UserService(IAppDbContextFactory dbFactory) : IUserService
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        return await db.Users.AsNoTracking()
            .Where(u => includeInactive || u.IsActive)
            .OrderBy(u => u.FullName).ThenBy(u => u.Email)
            .Select(u => new UserDto(u.Id, u.FullName, u.Email, u.IsActive))
            .ToListAsync(ct);
    }
}

internal static class UserRules
{
    public const string InactiveUserMessage = "Select an active user.";

    /// <summary>
    /// A newly assigned user must exist and be active; keeping an already assigned (now deactivated) user is allowed
    /// so old records stay editable (D-07).
    /// </summary>
    public static async Task<string?> ValidateAssigneeAsync(IAppDbContext db, string? userId, string? currentUserId, CancellationToken ct)
    {
        if (userId is null || userId == currentUserId)
        {
            return null;
        }

        var active = await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, ct);
        return active ? null : InactiveUserMessage;
    }
}
