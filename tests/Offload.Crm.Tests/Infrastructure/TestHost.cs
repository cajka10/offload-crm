using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Infrastructure.Seeding;

namespace Offload.Crm.Tests.Infrastructure;

/// <summary>The real app on the test DB, with the cookie validation interval at zero (session changes apply at once).</summary>
public static partial class TestHost
{
    public const string AdminEmail = "admin@offload.local";
    public const string AdminPassword = "Admin123!";

    public static async Task<WebApplicationFactory<Program>> CreateAsync(
        DatabaseFixture db, string environment = "Testing", Action<IServiceCollection>? services = null)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Default", db.ConnectionString);
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.UseSetting("Seed:DemoData", "false");
            builder.ConfigureTestServices(s =>
            {
                s.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
                services?.Invoke(s);
            });
        });

        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DbInitializer>().SeedAdminAsync();
        return factory;
    }

    public static async Task<ApplicationUser> CreateUserAsync(WebApplicationFactory<Program> factory, string email, string password, bool active = true)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, FullName = email.Split('@')[0], IsActive = active, EmailConfirmed = true };
        var result = await users.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    /// <summary>Signs in through the real login form; returns a client holding the cookie.</summary>
    public static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string email = AdminEmail, string password = AdminPassword)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var page = await client.GetStringAsync("/account/login");
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = Token(page),
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    public static string Token(string html) => TokenRegex().Match(html).Groups[1].Value;

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}
