using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Offload.Crm.Application.Abstractions;
using Offload.Crm.Application.Common;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Infrastructure.Persistence;

namespace Offload.Crm.Web.Auth;

/// <summary>D-19: a session is valid while the user exists, is active and its security stamp is unchanged.</summary>
public static class SessionValidation
{
    public static async Task<bool> IsValidAsync(UserManager<ApplicationUser> users, ClaimsPrincipal principal, IdentityOptions options)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var user = await users.GetUserAsync(principal);
        if (user is null || !user.IsActive)
        {
            return false;
        }

        if (!users.SupportsUserSecurityStamp)
        {
            return true;
        }

        return principal.FindFirstValue(options.ClaimsIdentity.SecurityStampClaimType) == await users.GetSecurityStampAsync(user);
    }
}

/// <summary>Cookie validation (every request after the interval) also rejects deactivated users (BUG-004).</summary>
public sealed class AppSecurityStampValidator(
    IOptions<SecurityStampValidatorOptions> options,
    SignInManager<ApplicationUser> signInManager,
    ILoggerFactory logger)
    : SecurityStampValidator<ApplicationUser>(options, signInManager, logger)
{
    protected override async Task<ApplicationUser?> VerifySecurityStamp(ClaimsPrincipal? principal)
    {
        var user = await base.VerifySecurityStamp(principal);
        return user is { IsActive: true } ? user : null;
    }
}

/// <summary>Marks the DI scope of a Blazor circuit, so services know they run inside an interactive session.</summary>
public sealed class CircuitContext
{
    public bool IsInCircuit { get; set; }

    /// <summary>D-26: the database that was active when the circuit opened.</summary>
    public DatabaseTarget Database { get; set; }
}

internal sealed class CircuitContextHandler(CircuitContext context, DatabaseSelector databases) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        context.IsInCircuit = true;
        context.Database = databases.Current;
        return Task.CompletedTask;
    }
}

/// <summary>The signed-in user of the circuit (interactive UI) or of the HTTP request (API, static pages).</summary>
internal sealed class CurrentUser(CircuitContext circuit, AuthenticationStateProvider auth, IHttpContextAccessor http) : ICurrentUser
{
    public async Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
        var principal = circuit.IsInCircuit
            ? (await auth.GetAuthenticationStateAsync()).User
            : http.HttpContext?.User;
        return principal?.Identity?.IsAuthenticated == true ? principal.FindFirstValue(ClaimTypes.NameIdentifier) : null;
    }
}

/// <summary>
/// Every service call from a circuit re-checks the session (BUG-003/004): after a logout in another tab or a
/// deactivation, the next action fails and the tab is sent to the login page. API requests are covered by the
/// cookie validation instead.
/// </summary>
internal sealed class CircuitSessionGuard(
    CircuitContext circuit,
    AuthenticationStateProvider auth,
    IServiceScopeFactory scopes,
    NavigationManager navigation,
    IOptions<IdentityOptions> identityOptions,
    DatabaseSelector databases)
{
    public async Task EnsureValidAsync()
    {
        if (!circuit.IsInCircuit)
        {
            return;
        }

        // D-26: a circuit stays on the database it started with (its scoped services hold contexts for it).
        if (databases.Current != circuit.Database)
        {
            // The active database was switched: reload the page so the new circuit uses the new database.
            navigation.NavigateTo(navigation.Uri, forceLoad: true);
            throw new SessionExpiredException();
        }

        var principal = (await auth.GetAuthenticationStateAsync()).User;
        await using var scope = scopes.CreateAsyncScope(); // fresh UserManager/DbContext: no stale cached stamp
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await SessionValidation.IsValidAsync(users, principal, identityOptions.Value))
        {
            return;
        }

        navigation.NavigateTo("account/login?expired=1", forceLoad: true);
        throw new SessionExpiredException();
    }
}

/// <summary>Creates the per-operation DbContext only after the session check passed.</summary>
internal sealed class GuardedDbContextFactory(IDbContextFactory<AppDbContext> inner, CircuitSessionGuard guard) : IAppDbContextFactory
{
    public async Task<IAppDbContext> CreateAsync(CancellationToken cancellationToken = default)
    {
        await guard.EnsureValidAsync();
        return await inner.CreateDbContextAsync(cancellationToken);
    }
}
