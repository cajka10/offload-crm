using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Abstractions;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Users;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Domain.Enums;
using Offload.Crm.Infrastructure.Seeding;
using Offload.Crm.Web.Auth;

namespace Offload.Crm.Web.Users;

/// <summary>
/// Settings → Users (D-24). Identity operations run in a fresh DI scope per call (Blazor circuits are long-lived);
/// every call goes through the session guard (via <see cref="IAppDbContextFactory"/>) and the Admin policy (D-27).
/// </summary>
internal sealed class UserAdminService(
    IAppDbContextFactory dbFactory,
    IServiceScopeFactory scopes,
    IUserContext userContext,
    IAuthorizationService authorization,
    SignInTickets tickets) : IUserAdminService
{
    public const string SelfDeactivateMessage = "You can't deactivate your own account.";
    public const string LastActiveMessage = "At least one active user must remain.";
    public const string SelfDemoteMessage = "You can't remove the administrator role from your own account.";
    public const string LastAdminMessage = "At least one active administrator must remain.";

    public async Task<IReadOnlyList<UserListItem>> ListAsync(UserListQuery query, CancellationToken ct = default)
    {
        var me = await AuthorizeAsync();
        await using var db = await dbFactory.CreateAsync(ct);
        var term = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim().ToLowerInvariant();
        var users = await db.Users.AsNoTracking()
            .Where(u => query.Active == null || u.IsActive == query.Active)
            .Where(u => term == null || u.FullName.ToLower().Contains(term) || (u.Email != null && u.Email.ToLower().Contains(term)))
            .OrderByDescending(u => u.IsActive).ThenBy(u => u.FullName).ThenBy(u => u.Email)
            .ToListAsync(ct);
        var admins = await AdminIdsAsync();
        return users.Select(u => ToItem(u, me, admins)).ToList();
    }

    public async Task<UserListItem> GetAsync(string id, CancellationToken ct = default)
    {
        var me = await AuthorizeAsync();
        await using var db = await dbFactory.CreateAsync(ct);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);
        return ToItem(user, me, await AdminIdsAsync());
    }

    public async Task<UserListItem> CreateAsync(UserCreateInput input, CancellationToken ct = default)
    {
        var me = await AuthorizeAsync();
        await using var guard = await dbFactory.CreateAsync(ct);
        input.Normalize();
        ModelValidator.ThrowIfInvalid(input);

        await using var scope = scopes.CreateAsyncScope();
        var users = Users(scope);
        var user = new ApplicationUser
        {
            UserName = input.Email,
            Email = input.Email,
            EmailConfirmed = true,
            FullName = input.FullName!,
            IsActive = true,
        };
        Check(await users.CreateAsync(user, input.Password!), passwordField: nameof(UserCreateInput.Password));
        if (input.IsAdmin)
        {
            Check(await users.AddToRoleAsync(user, Roles.Admin), passwordField: null);
        }

        return ToItem(user, me, input.IsAdmin ? [user.Id] : []);
    }

    public async Task<UserListItem> UpdateAsync(string id, UserUpdateInput input, CancellationToken ct = default)
    {
        var me = await AuthorizeAsync();
        await using var guard = await dbFactory.CreateAsync(ct);
        input.Normalize();
        ModelValidator.ThrowIfInvalid(input);

        await using var scope = scopes.CreateAsyncScope();
        var users = Users(scope);
        var user = await users.FindByIdAsync(id) ?? throw new NotFoundException("User", id);
        var wasAdmin = await users.IsInRoleAsync(user, Roles.Admin);
        if (wasAdmin && !input.IsAdmin)
        {
            await EnsureCanRevokeAdminAsync(users, user.Id, me); // before any change is saved
        }

        user.FullName = input.FullName!;
        if (!string.Equals(user.Email, input.Email, StringComparison.Ordinal))
        {
            user.Email = input.Email;
            user.UserName = input.Email; // the e-mail is the login name
            user.EmailConfirmed = true;
        }

        Check(await users.UpdateAsync(user), passwordField: null);
        if (input.IsAdmin != wasAdmin)
        {
            Check(
                input.IsAdmin ? await users.AddToRoleAsync(user, Roles.Admin) : await users.RemoveFromRoleAsync(user, Roles.Admin),
                passwordField: null);
        }

        return ToItem(user, me, input.IsAdmin ? [user.Id] : []);
    }

    public async Task<DeactivationImpact> GetDeactivationImpactAsync(string id, CancellationToken ct = default)
    {
        await AuthorizeAsync();
        await using var db = await dbFactory.CreateAsync(ct);
        if (!await db.Users.AnyAsync(u => u.Id == id, ct))
        {
            throw new NotFoundException("User", id);
        }

        var openTasks = await db.Tasks.CountAsync(t => t.AssignedUserId == id && t.Status != TaskItemStatus.Done, ct);
        var activeProjects = await db.Projects.CountAsync(p => p.ResponsibleUserId == id && p.Status.IsActive, ct);
        return new DeactivationImpact(openTasks, activeProjects);
    }

    public async Task DeactivateAsync(string id, CancellationToken ct = default)
    {
        var me = await AuthorizeAsync();
        await using var db = await dbFactory.CreateAsync(ct);
        if (id == me)
        {
            throw new ConflictException(SelfDeactivateMessage);
        }

        await using var scope = scopes.CreateAsyncScope();
        var users = Users(scope);
        var user = await users.FindByIdAsync(id) ?? throw new NotFoundException("User", id);
        if (!user.IsActive)
        {
            return;
        }

        if (!await db.Users.AnyAsync(u => u.IsActive && u.Id != id, ct))
        {
            throw new ConflictException(LastActiveMessage);
        }

        if (await users.IsInRoleAsync(user, Roles.Admin) && !await HasOtherActiveAdminAsync(users, id))
        {
            throw new ConflictException(LastAdminMessage);
        }

        user.IsActive = false;
        Check(await users.UpdateAsync(user), passwordField: null);
        await users.UpdateSecurityStampAsync(user); // ends all of their sessions (D-19)
    }

    public async Task ReactivateAsync(string id, CancellationToken ct = default)
    {
        await AuthorizeAsync();
        await using var guard = await dbFactory.CreateAsync(ct);
        await using var scope = scopes.CreateAsyncScope();
        var users = Users(scope);
        var user = await users.FindByIdAsync(id) ?? throw new NotFoundException("User", id);
        user.IsActive = true;
        Check(await users.UpdateAsync(user), passwordField: null);
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
    }

    public async Task ResetPasswordAsync(string id, PasswordResetInput input, CancellationToken ct = default)
    {
        await AuthorizeAsync();
        await using var guard = await dbFactory.CreateAsync(ct);
        ModelValidator.ThrowIfInvalid(input);

        await using var scope = scopes.CreateAsyncScope();
        var users = Users(scope);
        var user = await users.FindByIdAsync(id) ?? throw new NotFoundException("User", id);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        Check(await users.ResetPasswordAsync(user, token, input.Password!), passwordField: nameof(PasswordResetInput.Password)); // also rotates the stamp
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
    }

    public async Task<string> ChangeOwnPasswordAsync(ChangeOwnPasswordInput input, CancellationToken ct = default)
    {
        var me = await AuthorizeAsync(Policies.ActiveUser); // every user may change their own password
        await using var guard = await dbFactory.CreateAsync(ct);
        ModelValidator.ThrowIfInvalid(input);

        await using var scope = scopes.CreateAsyncScope();
        var users = Users(scope);
        var user = await users.FindByIdAsync(me) ?? throw new SessionExpiredException();
        var result = await users.ChangePasswordAsync(user, input.CurrentPassword!, input.NewPassword!); // rotates the stamp
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
        {
            throw new AppValidationException(nameof(ChangeOwnPasswordInput.CurrentPassword), "The current password is not correct.");
        }

        Check(result, passwordField: nameof(ChangeOwnPasswordInput.NewPassword));
        return tickets.Issue(user.Id);
    }

    private async Task<string> AuthorizeAsync(string policy = Policies.Admin)
    {
        var principal = await userContext.GetPrincipalAsync();
        var result = await authorization.AuthorizeAsync(principal, policy);
        if (!result.Succeeded)
        {
            throw new ForbiddenException("Only administrators can manage users.");
        }

        return principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
    }

    private static UserManager<ApplicationUser> Users(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    /// <summary>D-27: the Admin role is never removed from yourself or from the last active administrator.</summary>
    private static async Task EnsureCanRevokeAdminAsync(UserManager<ApplicationUser> users, string userId, string me)
    {
        if (userId == me)
        {
            throw new ConflictException(SelfDemoteMessage);
        }

        if (!await HasOtherActiveAdminAsync(users, userId))
        {
            throw new ConflictException(LastAdminMessage);
        }
    }

    private static async Task<bool> HasOtherActiveAdminAsync(UserManager<ApplicationUser> users, string userId) =>
        (await users.GetUsersInRoleAsync(Roles.Admin)).Any(u => u.IsActive && u.Id != userId);

    private async Task<HashSet<string>> AdminIdsAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        return (await Users(scope).GetUsersInRoleAsync(Roles.Admin)).Select(u => u.Id).ToHashSet();
    }

    private static UserListItem ToItem(ApplicationUser u, string me, IReadOnlyCollection<string> adminIds) =>
        new(u.Id, u.FullName, u.Email, u.IsActive, u.CreatedAt, u.LastSignInAt, u.Id == me, adminIds.Contains(u.Id));

    /// <summary>Maps Identity errors to field errors (D-24).</summary>
    private static void Check(IdentityResult result, string? passwordField)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) && passwordField is not null
                ? passwordField
                : e.Code is "DuplicateEmail" or "DuplicateUserName" or "InvalidEmail" or "InvalidUserName" ? "Email" : string.Empty)
            .ToDictionary(g => g.Key, g => g.Select(e => Describe(e)).Distinct().ToArray());
        throw new AppValidationException(errors);
    }

    private static string Describe(IdentityError e) => e.Code switch
    {
        "DuplicateEmail" or "DuplicateUserName" => "A user with this e-mail already exists.",
        _ => e.Description,
    };
}
