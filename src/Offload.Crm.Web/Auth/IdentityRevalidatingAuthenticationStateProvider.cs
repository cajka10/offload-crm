using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Offload.Crm.Domain.Entities;

namespace Offload.Crm.Web.Auth;

/// <summary>
/// Re-checks the circuit's user every minute (security stamp + active flag, D-19). When it fails, the auth state
/// becomes anonymous and the router sends the tab to the login page.
/// </summary>
internal sealed class IdentityRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> options)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override TimeSpan RevalidationInterval => Interval;

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await SessionValidation.IsValidAsync(userManager, authenticationState.User, options.Value);
    }
}
