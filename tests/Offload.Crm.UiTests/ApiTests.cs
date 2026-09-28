using System.Text.Json;
using Microsoft.Playwright;
using Offload.Crm.UiTests.Infrastructure;

namespace Offload.Crm.UiTests;

/// <summary>Black-box checks of the REST API of the running app (cookie auth from the admin sign-in).</summary>
[Collection(UiCollection.Name)]
public sealed class ApiTests(PlaywrightFixture fx)
{
    [UiTheory]
    [InlineData("/api/customers")]
    [InlineData("/api/customers/1")]
    [InlineData("/api/customers/1/contacts")]
    [InlineData("/api/contacts/1")]
    [InlineData("/api/does-not-exist")]
    public async Task Api_without_cookie_returns_401_not_a_redirect(string path)
    {
        await using var context = await fx.NewAnonymousContextAsync();
        var response = await context.APIRequest.GetAsync(path, new() { MaxRedirects = 0 });
        Assert.Equal(401, response.Status);
    }

    [UiTheory]
    [InlineData("/api/customers/987654321")]
    [InlineData("/api/customers/-1")]
    [InlineData("/api/customers/987654321/contacts")]
    [InlineData("/api/contacts/987654321")]
    [InlineData("/api/customers/abc")]
    [InlineData("/api/nothing-here")]
    public async Task Unknown_ids_and_routes_return_404_problem_details(string path)
    {
        await using var context = await fx.NewAdminContextAsync();
        var response = await context.APIRequest.GetAsync(path);
        Assert.Equal(404, response.Status);
        Assert.StartsWith("application/problem+json", response.Headers["content-type"]);
    }

    [UiFact]
    public async Task Create_with_invalid_fields_returns_400_with_camel_case_field_errors()
    {
        await using var context = await fx.NewAdminContextAsync();
        var response = await context.APIRequest.PostAsync("/api/customers", new()
        {
            DataObject = new { companyName = "   ", email = "a@b", website = "not a url", phone = "abc", registrationNo = new string('1', 21) },
        });

        Assert.Equal(400, response.Status);
        var errors = (await response.JsonAsync())!.Value.GetProperty("errors");
        foreach (var field in new[] { "companyName", "email", "website", "phone", "registrationNo" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"missing error for {field}: {errors}");
        }
    }

    [UiFact]
    public async Task Duplicate_registration_no_variants_are_rejected_and_concurrent_creates_create_one()
    {
        await using var context = await fx.NewAdminContextAsync();
        var ico = "QA" + Random.Shared.Next(100000, 999999);
        var created = await context.APIRequest.PostAsync("/api/customers", new() { DataObject = new { companyName = "API dup base", registrationNo = ico } });
        Assert.Equal(201, created.Status);
        var id = (await created.JsonAsync())!.Value.GetProperty("id").GetInt32();

        try
        {
            foreach (var variant in new[] { ico.ToLowerInvariant(), $"  {ico}  ", string.Join(' ', ico.ToCharArray()) })
            {
                var dup = await context.APIRequest.PostAsync("/api/customers", new() { DataObject = new { companyName = "API dup", registrationNo = variant } });
                Assert.Equal(400, dup.Status);
            }

            // Parallel double submit with a fresh IČO: exactly one wins, the others get 400 (not 500).
            var ico2 = ico + "X";
            var tasks = Enumerable.Range(0, 6).Select(_ => context.APIRequest.PostAsync("/api/customers", new() { DataObject = new { companyName = "API race", registrationNo = ico2 } }));
            var results = await Task.WhenAll(tasks);
            Assert.Equal(1, results.Count(r => r.Status == 201));
            Assert.All(results.Where(r => r.Status != 201), r => Assert.Equal(400, r.Status));
            var raceId = (await results.Single(r => r.Status == 201).JsonAsync())!.Value.GetProperty("id").GetInt32();
            await context.APIRequest.DeleteAsync($"/api/customers/{raceId}");
        }
        finally
        {
            await context.APIRequest.DeleteAsync($"/api/customers/{id}");
        }
    }

    [UiFact]
    public async Task Delete_customer_with_projects_returns_409_and_keeps_it()
    {
        await using var context = await fx.NewAdminContextAsync();
        var list = await context.APIRequest.GetAsync("/api/customers?sort=projectCount&descending=true&pageSize=1");
        var first = (await list.JsonAsync())!.Value.GetProperty("items")[0];
        Assert.True(first.GetProperty("projectCount").GetInt32() > 0, "Seed data should contain a customer with projects.");
        var id = first.GetProperty("id").GetInt32();

        var response = await context.APIRequest.DeleteAsync($"/api/customers/{id}");

        Assert.Equal(409, response.Status);
        Assert.Contains("Delete or reassign them first", await response.TextAsync());
        Assert.Equal(200, (await context.APIRequest.GetAsync($"/api/customers/{id}")).Status);
    }

    [UiTheory]
    [InlineData("page=-5&pageSize=-1")]
    [InlineData("page=0&pageSize=0")]
    [InlineData("pageSize=100000")]
    [InlineData("sort=nonsense&descending=true")]
    [InlineData("search=%27%20OR%201%3D1--")]
    [InlineData("search=%25")]
    public async Task Odd_paging_sort_and_search_params_are_normalized(string query)
    {
        await using var context = await fx.NewAdminContextAsync();
        var response = await context.APIRequest.GetAsync("/api/customers?" + query);
        Assert.Equal(200, response.Status);
        var body = (await response.JsonAsync())!.Value;
        Assert.InRange(body.GetProperty("pageSize").GetInt32(), 1, 100);
        Assert.True(body.GetProperty("page").GetInt32() >= 1);
    }

    [UiFact]
    public async Task Sql_like_search_matches_nothing_instead_of_everything()
    {
        await using var context = await fx.NewAdminContextAsync();
        var response = await context.APIRequest.GetAsync("/api/customers?search=" + Uri.EscapeDataString("' OR 1=1 --"));
        Assert.Equal(0, (await response.JsonAsync())!.Value.GetProperty("totalCount").GetInt32());
    }

    [UiFact]
    public async Task Oversized_payload_is_rejected_with_400()
    {
        await using var context = await fx.NewAdminContextAsync();
        var response = await context.APIRequest.PostAsync("/api/customers", new()
        {
            DataObject = new { companyName = "API huge", notes = new string('x', 2_000_000) },
        });
        Assert.Equal(400, response.Status);
    }

    [UiFact]
    public async Task Html_is_stored_verbatim_and_returned_as_json_text()
    {
        await using var context = await fx.NewAdminContextAsync();
        const string name = "<script>alert(1)</script> API";
        var response = await context.APIRequest.PostAsync("/api/customers", new() { DataObject = new { companyName = name } });
        Assert.Equal(201, response.Status);
        var json = (await response.JsonAsync())!.Value;
        Assert.Equal(name, json.GetProperty("companyName").GetString());
        await context.APIRequest.DeleteAsync($"/api/customers/{json.GetProperty("id").GetInt32()}");
    }
}
