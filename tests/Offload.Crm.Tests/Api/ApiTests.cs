using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Infrastructure.Seeding;
using Offload.Crm.Tests.Infrastructure;

namespace Offload.Crm.Tests.Api;

/// <summary>End-to-end tests through the real HTTP pipeline: cookie auth (T-04) and the customers API (T-07/T-09).</summary>
[Collection(DatabaseCollection.Name)]
public sealed partial class ApiTests(DatabaseFixture db) : IAsyncLifetime
{
    private const string AdminEmail = "admin@offload.local";
    private const string AdminPassword = "Admin123!";

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", db.ConnectionString);
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.UseSetting("Seed:AdminEmail", AdminEmail);
            builder.UseSetting("Seed:AdminPassword", AdminPassword);
        });

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DbInitializer>().SeedAdminAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
    {
        var page = await client.GetStringAsync("/account/login");
        var token = TokenRegex().Match(page).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "antiforgery token not found on login page");

        return await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = token,
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }));
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        var response = await LoginAsync(client, AdminEmail, AdminPassword);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    [Fact]
    public async Task Api_without_auth_returns_401()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Page_without_auth_redirects_to_login_with_return_url()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/customers?search=abc");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/login", response.Headers.Location!.AbsolutePath);
        Assert.Contains("ReturnUrl=%2Fcustomers%3Fsearch%3Dabc", response.Headers.Location.Query);
    }

    [Fact]
    public async Task Login_page_is_public()
    {
        var response = await CreateClient().GetAsync("/account/login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-testid=\"login-submit\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Api_with_auth_returns_200()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, json.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Wrong_password_shows_friendly_error()
    {
        var client = CreateClient();

        var response = await LoginAsync(client, AdminEmail, "wrong-password");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid e-mail or password.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Five_failed_attempts_lock_the_account()
    {
        var client = CreateClient();
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(client, AdminEmail, "wrong-password");
        }

        var response = await LoginAsync(client, AdminEmail, AdminPassword);

        Assert.Contains("locked", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Inactive_user_cannot_log_in()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = "gone@offload.local", Email = "gone@offload.local", FullName = "Gone", IsActive = false };
            Assert.True((await users.CreateAsync(user, "Gone1234!")).Succeeded);
        }

        var client = CreateClient();
        var response = await LoginAsync(client, "gone@offload.local", "Gone1234!");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("deactivated", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customers")).StatusCode);

        // A wrong password must not reveal that the account exists but is deactivated.
        var wrong = await LoginAsync(CreateClient(), "gone@offload.local", "Wrong1234!");
        var wrongHtml = await wrong.Content.ReadAsStringAsync();
        Assert.Contains("Invalid e-mail or password.", wrongHtml);
        Assert.DoesNotContain("deactivated", wrongHtml);
    }

    [Fact]
    public async Task Customer_crud_and_problem_details()
    {
        var client = await CreateAuthenticatedClientAsync();

        // 400 with field errors (camelCase keys)
        var invalid = await client.PostAsJsonAsync("/api/customers", new { companyName = " ", email = "bad" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var problem = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()))
        {
            var errors = problem.RootElement.GetProperty("errors");
            Assert.True(errors.TryGetProperty("companyName", out _));
            Assert.True(errors.TryGetProperty("email", out _));
        }

        // 201 + Location
        var created = await client.PostAsJsonAsync("/api/customers", new { companyName = "Nová firma ř", registrationNo = "123 45", status = "Active" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal($"/api/customers/{id}", created.Headers.Location!.ToString());

        // duplicate IČO → 400 on registrationNo
        var duplicate = await client.PostAsJsonAsync("/api/customers", new { companyName = "Dup", registrationNo = "12345" });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("registrationNo", await duplicate.Content.ReadAsStringAsync());

        // update + get
        var updated = await client.PutAsJsonAsync($"/api/customers/{id}", new { companyName = "Renamed", status = "Inactive" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/customers/{id}");
        Assert.Equal("Renamed", fetched.GetProperty("companyName").GetString());
        Assert.Equal("Inactive", fetched.GetProperty("status").GetString());

        // contacts
        var contact = await client.PostAsJsonAsync($"/api/customers/{id}/contacts", new { firstName = "Jan", lastName = "Novák" });
        Assert.Equal(HttpStatusCode.Created, contact.StatusCode);
        var contactId = (await contact.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/contacts/{contactId}", new { lastName = "Nováková" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/contacts/{contactId}", new { lastName = "" })).StatusCode);
        var contacts = await client.GetFromJsonAsync<JsonElement>($"/api/customers/{id}/contacts");
        Assert.Equal("Nováková", contacts[0].GetProperty("lastName").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/contacts/{contactId}")).StatusCode);

        // delete, then 404
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/customers/{id}")).StatusCode);
        var missing = await client.GetAsync($"/api/customers/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Delete_customer_with_projects_returns_409()
    {
        var customer = await db.AddCustomerAsync("Busy");
        await db.AddProjectAsync(customer.Id);
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync($"/api/customers/{customer.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Customer has 1 project.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task List_supports_query_parameters()
    {
        await db.AddCustomerAsync("Alfa", status: Domain.Enums.CustomerStatus.Active);
        await db.AddCustomerAsync("Beta");
        var client = await CreateAuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<JsonElement>("/api/customers?status=Active&sort=companyName&descending=true&page=1&pageSize=10");

        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
        Assert.Equal("Alfa", result.GetProperty("items")[0].GetProperty("companyName").GetString());
    }

    [Fact]
    public async Task Unknown_api_route_returns_404_problem()
    {
        var client = await CreateAuthenticatedClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/customers/abc")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/nothing-here")).StatusCode);
    }

    [Fact]
    public async Task Logout_clears_the_session()
    {
        var client = await CreateAuthenticatedClientAsync();
        var page = await client.GetStringAsync("/account/logout");
        var token = TokenRegex().Match(page).Groups[1].Value;

        var response = await client.PostAsync("/account/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "logout",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customers")).StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}
