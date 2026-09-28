using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Offload.Crm.Tests.Infrastructure;

namespace Offload.Crm.Tests.Api;

/// <summary>T-27/T-28 API contract (D-25) and the T-22 API bugs (BUG-019, BUG-025).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class Phase3ApiTests(DatabaseFixture db) : IAsyncLifetime
{
    private static readonly string[] ListEndpoints = ["/api/customers", "/api/contacts", "/api/projects", "/api/tasks", "/api/activities", "/api/users"];

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _factory = await TestHost.CreateAsync(db);
        _client = await TestHost.SignInAsync(_factory);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Every_list_endpoint_returns_the_envelope()
    {
        foreach (var url in ListEndpoints)
        {
            var json = await _client.GetFromJsonAsync<JsonElement>(url);
            Assert.True(json.TryGetProperty("items", out var items), url);
            Assert.Equal(JsonValueKind.Array, items.ValueKind);
            Assert.True(json.TryGetProperty("totalCount", out _), url);
            Assert.True(json.TryGetProperty("page", out _), url);
            Assert.True(json.TryGetProperty("pageSize", out _), url);
        }
    }

    [Fact]
    public async Task Anonymous_calls_get_401_problem_details()
    {
        using var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        foreach (var url in ListEndpoints.Append("/api/dashboard").Append("/api/project-statuses").Append("/api/users/x"))
        {
            var response = await anonymous.GetAsync(url);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Theory]
    [InlineData("PATCH", "/api/customers/1")]
    [InlineData("PUT", "/api/tasks")]
    [InlineData("DELETE", "/api/users/abc")]
    [InlineData("POST", "/api/dashboard")]
    public async Task Wrong_verb_is_405(string method, string url)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(new { }) });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Bug025_unknown_parameters_are_rejected_and_aliases_accepted()
    {
        var unknown = await _client.GetAsync("/api/projects?statuz=1");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("statuz", await unknown.Content.ReadAsStringAsync());

        var customer = await db.AddCustomerAsync("Alias C");
        await db.AddProjectAsync(customer.Id, "Alias P");
        var canonical = await _client.GetFromJsonAsync<JsonElement>($"/api/projects?customer={customer.Id}&pageSize=5&descending=true");
        var alias = await _client.GetFromJsonAsync<JsonElement>($"/api/projects?customerId={customer.Id}&size=5&desc=true");
        Assert.Equal(1, canonical.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, alias.GetProperty("totalCount").GetInt32());
        Assert.Equal(5, alias.GetProperty("pageSize").GetInt32());

        var lead = await db.StatusIdAsync("Lead");
        Assert.Equal(1, (await _client.GetFromJsonAsync<JsonElement>($"/api/projects?status={lead}")).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await _client.GetFromJsonAsync<JsonElement>($"/api/projects?statusId={lead}&statusId=999")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Bug025_tasks_default_to_open_like_the_ui()
    {
        var customer = await db.AddCustomerAsync("C");
        var project = await db.AddProjectAsync(customer.Id);
        await db.AddTaskAsync(project.Id, "Open");
        await db.AddTaskAsync(project.Id, "Done", Domain.Enums.TaskItemStatus.Done);

        async Task<int> Count(string query) =>
            (await _client.GetFromJsonAsync<JsonElement>("/api/tasks" + query)).GetProperty("totalCount").GetInt32();

        Assert.Equal(1, await Count(string.Empty));
        Assert.Equal(1, await Count("?status=open"));
        Assert.Equal(2, await Count("?status=all"));
        Assert.Equal(1, await Count("?status=Done"));
        Assert.Equal(2, await Count("?status=Todo,Done"));
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/tasks?status=all,Done")).StatusCode);
    }

    [Fact]
    public async Task Bug019_patch_status_requires_a_status()
    {
        var customer = await db.AddCustomerAsync("C");
        var project = await db.AddProjectAsync(customer.Id);
        var task = await db.AddTaskAsync(project.Id, "T", Domain.Enums.TaskItemStatus.Done);

        var empty = await _client.PatchAsJsonAsync($"/api/tasks/{task.Id}/status", new { });
        var typo = await _client.PatchAsJsonAsync($"/api/tasks/{task.Id}/status", new { state = "Todo" });
        var projectEmpty = await _client.PatchAsJsonAsync($"/api/projects/{project.Id}/status", new { });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("status", await empty.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, typo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, projectEmpty.StatusCode);
        var after = await _client.GetFromJsonAsync<JsonElement>($"/api/tasks/{task.Id}");
        Assert.Equal("Done", after.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Contacts_have_a_flat_create_and_list()
    {
        var customer = await db.AddCustomerAsync("C");

        var created = await _client.PostAsJsonAsync("/api/contacts", new { customerId = customer.Id, lastName = "Nová" });
        var missing = await _client.PostAsJsonAsync("/api/contacts", new { lastName = "X" });
        var list = await _client.GetFromJsonAsync<JsonElement>($"/api/contacts?customer={customer.Id}&search=nov");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Dashboard_and_users_endpoints()
    {
        var dashboard = await _client.GetFromJsonAsync<JsonElement>("/api/dashboard");
        Assert.True(dashboard.GetProperty("kpis").TryGetProperty("activeCustomers", out _));

        var users = await _client.GetFromJsonAsync<JsonElement>("/api/users?active=true");
        var admin = users.GetProperty("items")[0];
        Assert.Equal(TestHost.AdminEmail, admin.GetProperty("email").GetString());
        var raw = await _client.GetStringAsync($"/api/users/{admin.GetProperty("id").GetString()}");
        Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/users/nope")).StatusCode);
    }

    [Fact]
    public async Task Health_is_anonymous()
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Production", HttpStatusCode.NotFound)]
    [InlineData("Development", HttpStatusCode.OK)]
    public async Task Swagger_only_in_development(string environment, HttpStatusCode expected)
    {
        await using var factory = await TestHost.CreateAsync(db, environment);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var doc = await response.Content.ReadAsStringAsync();
            Assert.Contains("/api/projects", doc);
            Assert.Contains("pageSize", doc);
        }
    }
}
