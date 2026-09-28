using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Infrastructure.Seeding;
using Offload.Crm.Tests.Infrastructure;

namespace Offload.Crm.Tests.Api;

/// <summary>
/// BUG-003/004 (D-19): logout ends the user's other sessions and a deactivated user's cookie stops working.
/// The cookie validation interval (1 min in the app) is set to zero here so the effect is immediate.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed partial class SessionTests(DatabaseFixture db) : IAsyncLifetime
{
    private const string Email = "session@offload.local";
    private const string Password = "Session123!";

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", db.ConnectionString);
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.ConfigureTestServices(services =>
                services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero));
        });

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DbInitializer>().SeedAdminAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.CreateAsync(new ApplicationUser { UserName = Email, Email = Email, FullName = "Session User" }, Password)).Succeeded);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<HttpClient> SignInAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var page = await client.GetStringAsync("/account/login");
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = TokenRegex().Match(page).Groups[1].Value,
            ["Input.Email"] = Email,
            ["Input.Password"] = Password,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/customers")).StatusCode);
        return client;
    }

    [Fact]
    public async Task Deactivated_user_session_is_rejected()
    {
        var client = await SignInAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(Email);
            user!.IsActive = false;
            await users.UpdateAsync(user);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customers")).StatusCode);
        var page = await client.GetAsync("/customers");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
    }

    [Fact]
    public async Task Logout_in_one_browser_ends_the_other_sessions()
    {
        var first = await SignInAsync();
        var second = await SignInAsync();

        var confirm = await second.GetStringAsync("/account/logout");
        Assert.DoesNotContain("submit()", confirm); // BUG-011: no auto-submit
        var logout = await second.PostAsync("/account/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "logout",
            ["__RequestVerificationToken"] = TokenRegex().Match(confirm).Groups[1].Value,
        }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/customers")).StatusCode);
    }

    [Fact]
    public async Task Get_on_logout_does_not_sign_out()
    {
        var client = await SignInAsync();

        var confirm = await client.GetAsync("/account/logout");

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/customers")).StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}
