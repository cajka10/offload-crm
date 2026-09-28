using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Offload.Crm.Application.Common;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Infrastructure.Seeding;

namespace Offload.Crm.Web.Auth;

public static class Policies
{
    /// <summary>Any active signed-in user (e.g. changing their own password).</summary>
    public const string ActiveUser = "ActiveUser";

    /// <summary>
    /// D-27: administrators only — user management, the database switch and project status configuration.
    /// Role membership is read from the DB on every check, so granting/revoking applies immediately.
    /// </summary>
    public const string Admin = "Admin";

    public static void AddAppPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(ActiveUser, policy => policy.RequireAuthenticatedUser().AddRequirements(new ActiveUserRequirement(AdminOnly: false)));
        options.AddPolicy(Admin, policy => policy.RequireAuthenticatedUser().AddRequirements(new ActiveUserRequirement(AdminOnly: true)));
    }
}

public sealed record ActiveUserRequirement(bool AdminOnly) : IAuthorizationRequirement;

/// <summary>Checks the user's active flag (and Admin role) in the DB (fresh scope, no stale tracked user or claims).</summary>
internal sealed class ActiveUserHandler(IServiceScopeFactory scopes) : AuthorizationHandler<ActiveUserRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveUserRequirement requirement)
    {
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(id);
        if (user is { IsActive: true } && (!requirement.AdminOnly || await users.IsInRoleAsync(user, Roles.Admin)))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Server-side role checks for the interactive UI and the API (D-27).</summary>
public sealed class AdminGuard(IUserContext userContext, IAuthorizationService authorization)
{
    public async Task<bool> IsAdminAsync() =>
        (await authorization.AuthorizeAsync(await userContext.GetPrincipalAsync(), Policies.Admin)).Succeeded;

    public async Task EnsureAdminAsync(string action)
    {
        if (!await IsAdminAsync())
        {
            throw new ForbiddenException($"Only administrators can {action}.");
        }
    }
}

/// <summary>The principal of the current circuit or HTTP request.</summary>
public interface IUserContext
{
    Task<ClaimsPrincipal> GetPrincipalAsync();
}

internal sealed class UserContext(CircuitContext circuit, AuthenticationStateProvider auth, IHttpContextAccessor http) : IUserContext
{
    public async Task<ClaimsPrincipal> GetPrincipalAsync() =>
        circuit.IsInCircuit
            ? (await auth.GetAuthenticationStateAsync()).User
            : http.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
}

/// <summary>
/// One-time, short-lived tickets that let the static account page re-issue the auth cookie after the user changed
/// their own password in the interactive UI (which can't write cookies). Single use, 2 minutes, bound to a user.
/// </summary>
public sealed class SignInTickets(TimeProvider clock)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, (string UserId, DateTimeOffset Expires)> _tickets = new();

    public string Issue(string userId)
    {
        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tickets[ticket] = (userId, clock.GetUtcNow() + Lifetime);
        return ticket;
    }

    /// <summary>Returns the user id and removes the ticket, or null when unknown/expired.</summary>
    public string? Redeem(string? ticket)
    {
        if (ticket is null || !_tickets.TryRemove(ticket, out var entry))
        {
            return null;
        }

        return entry.Expires >= clock.GetUtcNow() ? entry.UserId : null;
    }
}
