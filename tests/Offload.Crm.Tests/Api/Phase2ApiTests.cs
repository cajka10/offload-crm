using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Offload.Crm.Infrastructure.Seeding;
using Offload.Crm.Tests.Infrastructure;

namespace Offload.Crm.Tests.Api;

/// <summary>ProblemDetails shapes (D-20.7, BUG-001/002/008) and the Phase 2 endpoints.</summary>
[Collection(DatabaseCollection.Name)]
public sealed partial class Phase2ApiTests(DatabaseFixture db) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production"); // no developer details in error responses
            builder.UseSetting("ConnectionStrings:Default", db.ConnectionString);
            builder.UseSetting("Database:MigrateOnStartup", "false");
        });

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DbInitializer>().SeedAdminAsync();
        }

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var page = await _client.GetStringAsync("/account/login");
        var token = TokenRegex().Match(page).Groups[1].Value;
        var login = await _client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = token,
            ["Input.Email"] = "admin@offload.local",
            ["Input.Password"] = "Admin123!",
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)expected, json.GetProperty("status").GetInt32());
        return json;
    }

    private Task<HttpResponseMessage> PostRawAsync(string url, string body) =>
        _client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));

    [Theory]
    [InlineData("{\"companyName\": ", null)]
    [InlineData("{\"companyName\":123}", "$.companyName")]
    [InlineData("[]", null)]
    public async Task Malformed_or_wrongly_typed_json_returns_readable_400(string body, string? path)
    {
        var problem = await ProblemAsync(await PostRawAsync("/api/customers", body), HttpStatusCode.BadRequest);

        var detail = problem.GetProperty("detail").GetString()!;
        Assert.Contains("JSON", detail);
        if (path is not null)
        {
            Assert.Contains(path, detail);
        }

        Assert.DoesNotContain("   at ", detail);
    }

    [Fact]
    public async Task Missing_body_and_bad_query_values_return_400_problem()
    {
        var emptyJson = await PostRawAsync("/api/customers", string.Empty);
        Assert.Equal("A JSON request body is required.", (await ProblemAsync(emptyJson, HttpStatusCode.BadRequest)).GetProperty("detail").GetString());

        var noBody = await _client.PostAsync("/api/customers", null);
        Assert.Equal("A JSON request body is required.", (await ProblemAsync(noBody, HttpStatusCode.BadRequest)).GetProperty("detail").GetString());
        await ProblemAsync(await PostRawAsync("/api/customers", "null"), HttpStatusCode.BadRequest);
        await ProblemAsync(await _client.GetAsync("/api/customers?status=bogus"), HttpStatusCode.BadRequest);
        await ProblemAsync(await _client.GetAsync("/api/customers?page=abc"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Undefined_enum_numbers_are_400_with_field_error()
    {
        var body = await ProblemAsync(await PostRawAsync("/api/customers", "{\"companyName\":\"X\",\"status\":99}"), HttpStatusCode.BadRequest);
        Assert.True(body.GetProperty("errors").TryGetProperty("status", out _));

        var query = await ProblemAsync(await _client.GetAsync("/api/customers?status=99"), HttpStatusCode.BadRequest);
        Assert.True(query.GetProperty("errors").TryGetProperty("status", out _));

        await ProblemAsync(await _client.GetAsync("/api/tasks?status=42"), HttpStatusCode.BadRequest);
        await ProblemAsync(await _client.GetAsync("/api/activities?type=77"), HttpStatusCode.BadRequest);
        await ProblemAsync(await _client.GetAsync("/api/projects?priority=9"), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/customers?page=100000000")]
    [InlineData("/api/customers?page=2147483647&pageSize=100")]
    [InlineData("/api/projects?page=100000000")]
    [InlineData("/api/tasks?page=100000000")]
    [InlineData("/api/activities?page=100000000")]
    public async Task Huge_page_numbers_return_an_empty_page(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Unauthenticated_api_error_is_problem_json()
    {
        using var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await ProblemAsync(await anonymous.GetAsync("/api/projects"), HttpStatusCode.Unauthorized);
        await ProblemAsync(await anonymous.GetAsync("/api/does-not-exist"), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unknown_route_is_404_and_wrong_verb_is_405_problem()
    {
        await ProblemAsync(await _client.GetAsync("/api/does-not-exist"), HttpStatusCode.NotFound);
        await ProblemAsync(await _client.GetAsync("/api/projects/abc"), HttpStatusCode.NotFound);

        var patch = await _client.PatchAsync("/api/customers/1", JsonContent.Create(new { }));
        await ProblemAsync(patch, HttpStatusCode.MethodNotAllowed);
        Assert.Contains("GET", string.Join(",", patch.Content.Headers.Allow.Concat(patch.Headers.TryGetValues("Allow", out var allow) ? allow : [])));
    }

    [Fact]
    public async Task Project_status_endpoints_validate_and_block_deletes()
    {
        var statuses = await _client.GetFromJsonAsync<JsonElement>("/api/project-statuses");
        Assert.Equal(8, statuses.GetArrayLength());
        var leadId = statuses[0].GetProperty("id").GetInt32();

        var invalid = await ProblemAsync(
            await _client.PostAsJsonAsync("/api/project-statuses", new { name = "lead", color = "red", isActive = true, isClosed = true }),
            HttpStatusCode.BadRequest);
        var errors = invalid.GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("color", out _));
        Assert.True(errors.TryGetProperty("isClosed", out _));

        var conflict = await ProblemAsync(await _client.DeleteAsync($"/api/project-statuses/{leadId}"), HttpStatusCode.Conflict);
        Assert.Contains("default", conflict.GetProperty("detail").GetString());

        await ProblemAsync(await _client.PostAsync($"/api/project-statuses/{leadId}/move?direction=sideways", null), HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/project-statuses/{leadId}/move?direction=down", null)).StatusCode);
        await ProblemAsync(await _client.GetAsync("/api/project-statuses/99999"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Project_task_activity_round_trip()
    {
        var customer = await db.AddCustomerAsync("API customer");

        var badProject = await ProblemAsync(
            await _client.PostAsJsonAsync("/api/projects", new { name = "", customerId = customer.Id, startDate = "2026-10-10", targetDate = "2026-10-01", estimatedValue = -1 }),
            HttpStatusCode.BadRequest);
        var projectErrors = badProject.GetProperty("errors");
        Assert.True(projectErrors.TryGetProperty("name", out _));
        Assert.True(projectErrors.TryGetProperty("targetDate", out _));
        Assert.True(projectErrors.TryGetProperty("estimatedValue", out _));

        var created = await _client.PostAsJsonAsync("/api/projects", new { name = "API project", customerId = customer.Id, priority = "High" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var project = await created.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = project.GetProperty("id").GetInt32();
        Assert.Equal("Lead", project.GetProperty("statusName").GetString());

        var statusId = await db.StatusIdAsync("Development");
        var patched = await _client.PatchAsJsonAsync($"/api/projects/{projectId}/status", new { statusId });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        var timeline = await _client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/timeline");
        Assert.Contains(timeline.EnumerateArray(), i => i.GetProperty("detail").GetString() == "Status changed: Lead → Development");

        var task = await _client.PostAsJsonAsync("/api/tasks", new { title = "API task", projectId });
        Assert.Equal(HttpStatusCode.Created, task.StatusCode);
        var taskId = (await task.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var done = await _client.PatchAsJsonAsync($"/api/tasks/{taskId}/status", new { status = "Done" });
        Assert.NotEqual(JsonValueKind.Null, (await done.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("completedAt").ValueKind);
        await ProblemAsync(await _client.PatchAsJsonAsync($"/api/tasks/{taskId}/status", new { status = 42 }), HttpStatusCode.BadRequest);
        await ProblemAsync(await _client.PatchAsJsonAsync("/api/tasks/987654/status", new { status = "Done" }), HttpStatusCode.NotFound);

        var otherCustomer = await db.AddCustomerAsync("Other");
        var mismatch = await ProblemAsync(
            await _client.PostAsJsonAsync("/api/activities", new { description = "x", customerId = otherCustomer.Id, projectId }),
            HttpStatusCode.BadRequest);
        Assert.True(mismatch.GetProperty("errors").TryGetProperty("projectId", out _));

        var activity = await _client.PostAsJsonAsync("/api/activities", new { description = "Phone call", type = "Call", projectId });
        Assert.Equal(HttpStatusCode.Created, activity.StatusCode);
        var activityJson = await activity.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(customer.Id, activityJson.GetProperty("customerId").GetInt32());
        Assert.Equal("Administrator", activityJson.GetProperty("authorName").GetString());

        var deleteStatus = await ProblemAsync(await _client.DeleteAsync($"/api/project-statuses/{statusId}"), HttpStatusCode.Conflict);
        Assert.Contains("1 project uses it", deleteStatus.GetProperty("detail").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/projects/{projectId}")).StatusCode);
        await ProblemAsync(await _client.GetAsync($"/api/tasks/{taskId}"), HttpStatusCode.NotFound);
        var kept = await _client.GetFromJsonAsync<JsonElement>($"/api/activities?customerId={customer.Id}");
        Assert.Equal(2, kept.GetProperty("totalCount").GetInt32()); // status-change note + phone call
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}
