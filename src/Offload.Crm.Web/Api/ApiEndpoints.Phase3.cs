using Offload.Crm.Application.Common;
using Offload.Crm.Application.Dashboard;
using Offload.Crm.Application.Users;
using Q = Offload.Crm.Web.Ui.QueryNames;

namespace Offload.Crm.Web.Api;

public static partial class ApiEndpoints
{
    private static void MapPhase3(RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", async (IDashboardService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(ct)))
            .WithTags("Dashboard")
            .WithSummary("Dashboard for the current user (D-23): KPIs, projects by status, deadlines, my tasks, recent activities, recently updated");

        var users = api.MapGroup("/users").WithTags("Users");

        // D-24: read-only; user writes are UI-only. Never exposes hashes or security stamps.
        users.MapGet("/", async (HttpRequest request, IUserAdminService service, CancellationToken ct) =>
            {
                var q = ApiQuery.Parse(request, paged: false, Q.Active, Q.Search);
                var items = (await service.ListAsync(new UserListQuery { Active = q.Bool(Q.Active), Search = q.String(Q.Search) }, ct))
                    .Select(ApiUser.From)
                    .ToList();
                return TypedResults.Ok(new PagedResult<ApiUser>(items, items.Count, 1, Math.Max(1, items.Count)));
            })
            .Produces<PagedResult<ApiUser>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithMetadata(new ApiQueryParameters(false, [(Q.Active, "true | false (default: all)"), (Q.Search, "name or e-mail contains")]))
            .WithSummary("List users, active first then by name (not paged)");

        users.MapGet("/{id}", async (string id, IUserAdminService service, CancellationToken ct) =>
                TypedResults.Ok(ApiUser.From(await service.GetAsync(id, ct))))
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}

/// <summary>User as exposed by the API (D-24).</summary>
public sealed record ApiUser(string Id, string FullName, string? Email, bool IsActive, DateTime? LastSignInAt)
{
    public static ApiUser From(UserListItem u) => new(u.Id, u.FullName, u.Email, u.IsActive, u.LastSignInAt);
}
