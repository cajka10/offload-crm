using Offload.Crm.Application.Activities;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Contacts;
using Offload.Crm.Application.Customers;
using Offload.Crm.Application.Projects;
using Offload.Crm.Application.ProjectStatuses;
using Offload.Crm.Application.Tasks;
using Offload.Crm.Domain.Enums;
using Offload.Crm.Web.Ui;
using Q = Offload.Crm.Web.Ui.QueryNames;

namespace Offload.Crm.Web.Api;

/// <summary>
/// REST API under <c>/api</c> (D-01, D-25). Same services and validation as the UI; cookie auth.
/// Lists take <c>search, sort, descending, page, pageSize</c> plus filters named like the UI URL keys and return
/// <c>{ items, totalCount, page, pageSize }</c>. Errors are problem+json (400/401/403/404/405/409/415).
/// </summary>
public static partial class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api")
            .RequireAuthorization()
            .AddEndpointFilter<ApiExceptionFilter>()
            .DisableAntiforgery();

        MapCustomers(api.MapGroup("/customers").WithTags("Customers"));
        MapContacts(api.MapGroup("/contacts").WithTags("Contacts"));
        MapProjectStatuses(api.MapGroup("/project-statuses").WithTags("Project statuses"));
        MapProjects(api.MapGroup("/projects").WithTags("Projects"));
        MapTasks(api.MapGroup("/tasks").WithTags("Tasks"));
        MapActivities(api.MapGroup("/activities").WithTags("Activities"));
        MapPhase3(api);

        return app;
    }

    private static void MapCustomers(RouteGroupBuilder group)
    {
        group.MapGet("/", async (HttpRequest request, ICustomerService service, CancellationToken ct) =>
            {
                var q = ApiQuery.Parse(request, paged: true, Q.Status);
                var query = q.Paged(new CustomerListQuery { Status = q.Enum<CustomerStatus>(Q.Status) });
                return TypedResults.Ok(await service.ListAsync(query, ct));
            })
            .Lists<CustomerListItem>((Q.Status, "Lead | Active | Inactive"))
            .WithSummary("List customers (sort: companyName, registrationNo, city, status, email, phone, projectCount, lastActivity, created)");

        group.MapGet("/{id:int}", async (int id, ICustomerService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(id, ct)))
            .Problems(404);

        group.MapPost("/", async (CustomerInput input, ICustomerService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(input, ct);
            return TypedResults.Created($"/api/customers/{created.Id}", created);
        }).Problems(400);

        group.MapPut("/{id:int}", async (int id, CustomerInput input, ICustomerService service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateAsync(id, input, ct))).Problems(400, 404);

        group.MapDelete("/{id:int}", async (int id, ICustomerService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        }).Problems(404, 409).WithSummary("Delete a customer (409 while it has projects, D-07)");

        group.MapGet("/{id:int}/contacts", async (int id, IContactService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListByCustomerAsync(id, ct))).Problems(404).WithSummary("All contacts of a customer (not paged)");

        group.MapPost("/{id:int}/contacts", async (int id, ContactInput input, IContactService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(id, input, ct);
            return TypedResults.Created($"/api/contacts/{created.Id}", created);
        }).Problems(400, 404);
    }

    private static void MapContacts(RouteGroupBuilder group)
    {
        group.MapGet("/", async (HttpRequest request, IContactService service, CancellationToken ct) =>
            {
                var q = ApiQuery.Parse(request, paged: true, Q.Customer);
                return TypedResults.Ok(await service.ListAsync(q.Paged(new ContactListQuery { CustomerId = q.Int(Q.Customer) }), ct));
            })
            .Lists<ContactDto>((Q.Customer, "customer id"))
            .WithSummary("List contacts (sort: name, customer, email, created)");

        group.MapGet("/{id:int}", async (int id, IContactService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(id, ct)))
            .Problems(404);

        group.MapPost("/", async (NewContact input, IContactService service, CancellationToken ct) =>
        {
            if (input.CustomerId is not { } customerId)
            {
                throw new AppValidationException("customerId", "Select a customer.");
            }

            var created = await service.CreateAsync(customerId, input.ToInput(), ct);
            return TypedResults.Created($"/api/contacts/{created.Id}", created);
        }).Problems(400, 404);

        group.MapPut("/{id:int}", async (int id, ContactInput input, IContactService service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateAsync(id, input, ct))).Problems(400, 404);

        group.MapDelete("/{id:int}", async (int id, IContactService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        }).Problems(404);
    }

    private static void MapProjectStatuses(RouteGroupBuilder group)
    {
        // A small reference list: returned as a plain array in SortOrder (not paged).
        group.MapGet("/", async (HttpRequest request, IProjectStatusService service, CancellationToken ct) =>
        {
            ApiQuery.Parse(request, paged: false);
            return TypedResults.Ok(await service.ListAsync(ct));
        }).WithSummary("All project statuses in SortOrder (not paged)");

        group.MapGet("/{id:int}", async (int id, IProjectStatusService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(id, ct))).Problems(404);

        group.MapPost("/", async (ProjectStatusInput input, IProjectStatusService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(input, ct);
            return TypedResults.Created($"/api/project-statuses/{created.Id}", created);
        }).Problems(400);

        group.MapPut("/{id:int}", async (int id, ProjectStatusInput input, IProjectStatusService service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateAsync(id, input, ct))).Problems(400, 404);

        group.MapDelete("/{id:int}", async (int id, IProjectStatusService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        }).Problems(404, 409).WithSummary("Delete a status (409 when in use or the default, D-02)");

        group.MapPost("/{id:int}/move", async (int id, HttpRequest request, IProjectStatusService service, CancellationToken ct) =>
        {
            var direction = ApiQuery.Parse(request, paged: false, "direction").String("direction");
            var up = direction?.ToLowerInvariant() switch
            {
                "up" => true,
                "down" => false,
                _ => throw new AppValidationException("direction", "Use direction=up or direction=down."),
            };
            await service.MoveAsync(id, up, ct);
            return TypedResults.Ok(await service.ListAsync(ct));
        }).Problems(400, 404).WithSummary("Move a status up or down (?direction=up|down)");

        group.MapPost("/{id:int}/default", async (int id, IProjectStatusService service, CancellationToken ct) =>
        {
            await service.SetDefaultAsync(id, ct);
            return TypedResults.Ok(await service.GetAsync(id, ct));
        }).Problems(404, 409).WithSummary("Make a status the default (409 for closed statuses, BUG-020)");
    }

    private static void MapProjects(RouteGroupBuilder group)
    {
        group.MapGet("/", async (HttpRequest request, IProjectService service, CancellationToken ct) =>
            {
                var q = ApiQuery.Parse(request, paged: true, Q.Status, Q.Customer, Q.Priority, Q.Responsible, Q.Active);
                var query = q.Paged(new ProjectListQuery
                {
                    StatusIds = q.Ints(Q.Status),
                    CustomerId = q.Int(Q.Customer),
                    Priority = q.Enum<Priority>(Q.Priority),
                    ResponsibleUserId = q.String(Q.Responsible),
                    ActiveOnly = q.Bool(Q.Active) ?? true,
                });
                return TypedResults.Ok(await service.ListAsync(query, ct));
            })
            .Lists<ProjectListItem>(
                (Q.Status, "status ids (repeat or comma-separate); when given, 'active' is not applied"),
                (Q.Customer, "customer id"),
                (Q.Priority, "Low | Medium | High | Critical"),
                (Q.Responsible, "user id"),
                (Q.Active, "true (default) = only projects in an active status"))
            .WithSummary("List projects (sort: name, customer, status, priority, responsible, start, target, estimatedValue, openTasks, created)");

        group.MapGet("/{id:int}", async (int id, IProjectService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(id, ct)))
            .Problems(404);

        group.MapGet("/{id:int}/timeline", async (int id, IProjectService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetTimelineAsync(id, ct))).Problems(404).WithSummary("Project timeline, newest first (D-14)");

        group.MapPost("/", async (ProjectInput input, IProjectService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(input, ct);
            return TypedResults.Created($"/api/projects/{created.Id}", created);
        }).Problems(400);

        group.MapPut("/{id:int}", async (int id, ProjectInput input, IProjectService service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateAsync(id, input, ct))).Problems(400, 404);

        group.MapPatch("/{id:int}/status", async (int id, ProjectStatusChange body, IProjectService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ChangeStatusAsync(id, body.StatusId ?? throw Required("statusId"), ct)))
            .Problems(400, 404).WithSummary("Change the status ({ \"statusId\": 3 }); logs a Note activity (D-19)");

        group.MapDelete("/{id:int}", async (int id, IProjectService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        }).Problems(404).WithSummary("Delete a project with its tasks; its activities are kept (D-07)");
    }

    private static void MapTasks(RouteGroupBuilder group)
    {
        group.MapGet("/", async (HttpRequest request, ITaskService service, CancellationToken ct) =>
            {
                var q = ApiQuery.Parse(request, paged: true, Q.Status, Q.Priority, Q.Assignee, Q.Project, Q.Customer, Q.Overdue);
                var query = q.Paged(new TaskListQuery
                {
                    Statuses = TaskStatuses(q),
                    Priority = q.Enum<Priority>(Q.Priority),
                    Assignee = q.String(Q.Assignee),
                    ProjectId = q.Int(Q.Project),
                    CustomerId = q.Int(Q.Customer),
                    OverdueOnly = q.Bool(Q.Overdue) ?? false,
                });
                return TypedResults.Ok(await service.ListAsync(query, ct));
            })
            .Lists<TaskDto>(
                (Q.Status, "open (default) | all | Todo, InProgress, Blocked, Done (repeat or comma-separate)"),
                (Q.Priority, "Low | Medium | High | Critical"),
                (Q.Assignee, "me | unassigned | user id"),
                (Q.Project, "project id"),
                (Q.Customer, "customer id"),
                (Q.Overdue, "true = due before today and not done"))
            .WithSummary("List tasks; open tasks by default like the UI (sort: title, project, customer, status, priority, assignee, due, created)");

        group.MapGet("/{id:int}", async (int id, ITaskService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(id, ct)))
            .Problems(404);

        group.MapPost("/", async (TaskInput input, ITaskService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(input, ct);
            return TypedResults.Created($"/api/tasks/{created.Id}", created);
        }).Problems(400);

        group.MapPut("/{id:int}", async (int id, TaskInput input, ITaskService service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateAsync(id, input, ct))).Problems(400, 404);

        // BUG-019: the status is required; a missing/misspelled field is a 400, not a silent reset to Todo.
        group.MapPatch("/{id:int}/status", async (int id, TaskStatusChange body, ITaskService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ChangeStatusAsync(id, body.Status ?? throw Required("status"), ct)))
            .Problems(400, 404).WithSummary("Change the status ({ \"status\": \"Done\" }); CompletedAt follows D-13");

        group.MapDelete("/{id:int}", async (int id, ITaskService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        }).Problems(404);
    }

    private static void MapActivities(RouteGroupBuilder group)
    {
        group.MapGet("/", async (HttpRequest request, IActivityService service, CancellationToken ct) =>
            {
                var q = ApiQuery.Parse(request, paged: true, Q.Type, Q.Customer, Q.Project, Q.Author, Q.From, Q.To);
                var query = q.Paged(new ActivityListQuery
                {
                    Type = q.Enum<ActivityType>(Q.Type),
                    CustomerId = q.Int(Q.Customer),
                    ProjectId = q.Int(Q.Project),
                    AuthorId = q.String(Q.Author),
                    From = q.Date(Q.From),
                    To = q.Date(Q.To),
                });
                return TypedResults.Ok(await service.ListAsync(query, ct));
            })
            .Lists<ActivityDto>(
                (Q.Type, "Call | Email | Meeting | Note | Other"),
                (Q.Customer, "customer id (incl. its projects' activities)"),
                (Q.Project, "project id"),
                (Q.Author, "user id"),
                (Q.From, "yyyy-MM-dd, local day (Europe/Bratislava), inclusive"),
                (Q.To, "yyyy-MM-dd, local day, inclusive"))
            .WithSummary("List activities, newest first by default (sort: date, type, customer, project, author, created)");

        group.MapGet("/{id:int}", async (int id, IActivityService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(id, ct)))
            .Problems(404);

        group.MapPost("/", async (ActivityInput input, IActivityService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(input, ct);
            return TypedResults.Created($"/api/activities/{created.Id}", created);
        }).Problems(400).WithSummary("Create an activity; the author is the current user. A date without offset is Europe/Bratislava time");

        group.MapPut("/{id:int}", async (int id, ActivityInput input, IActivityService service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateAsync(id, input, ct))).Problems(400, 404);

        group.MapDelete("/{id:int}", async (int id, IActivityService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        }).Problems(404);
    }

    /// <summary>Task status filter: absent/"open" = not Done (same default as the UI), "all" = no filter.</summary>
    private static IReadOnlyList<TaskItemStatus>? TaskStatuses(ApiQuery q)
    {
        var raw = q.Strings(Q.Status);
        if (raw.Count == 0 || raw.Any(s => s.Equals("open", StringComparison.OrdinalIgnoreCase)))
        {
            return raw.Count <= 1 ? TaskListQuery.OpenStatuses : throw new AppValidationException(Q.Status, "'open' can't be combined with other values.");
        }

        if (raw.Any(s => s.Equals("all", StringComparison.OrdinalIgnoreCase)))
        {
            return raw.Count == 1 ? null : throw new AppValidationException(Q.Status, "'all' can't be combined with other values.");
        }

        return q.Enums<TaskItemStatus>(Q.Status);
    }

    private static AppValidationException Required(string field) => new(field, $"'{field}' is required.");

    private static RouteHandlerBuilder Problems(this RouteHandlerBuilder builder, params int[] codes)
    {
        foreach (var code in codes)
        {
            builder = code == StatusCodes.Status400BadRequest ? builder.ProducesValidationProblem() : builder.ProducesProblem(code);
        }

        return builder;
    }

    /// <summary>Declares a paged list endpoint: envelope type, query parameters (for OpenAPI) and 400 for bad parameters.</summary>
    private static RouteHandlerBuilder Lists<T>(this RouteHandlerBuilder builder, params (string Name, string Description)[] filters) =>
        builder.Produces<PagedResult<T>>().ProducesValidationProblem().WithMetadata(new ApiQueryParameters(true, filters));
}

/// <summary>Body of <c>PATCH /api/projects/{id}/status</c>.</summary>
public sealed record ProjectStatusChange(int? StatusId);

/// <summary>Body of <c>POST /api/contacts</c>: a contact plus the customer it belongs to.</summary>
public sealed class NewContact
{
    public int? CustomerId { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Position { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Notes { get; set; }

    public ContactInput ToInput() => new()
    {
        FirstName = FirstName,
        LastName = LastName,
        Position = Position,
        Email = Email,
        Phone = Phone,
        Notes = Notes,
    };
}

/// <summary>Maps service exceptions to RFC 7807 responses: 400 (field errors), 401, 403, 404, 409.</summary>
public sealed class ApiExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (AppValidationException ex)
        {
            return Results.ValidationProblem(ToCamelCaseKeys(ex.Errors), title: ex.Message);
        }
        catch (NotFoundException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: ex.Message);
        }
        catch (ConflictException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: ex.Message);
        }
        catch (ForbiddenException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: ex.Message);
        }
        catch (SessionExpiredException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: ex.Message);
        }
    }

    /// <summary>Error keys match the JSON (camelCase) property names of the request body.</summary>
    private static Dictionary<string, string[]> ToCamelCaseKeys(IReadOnlyDictionary<string, string[]> errors) =>
        errors.ToDictionary(
            e => string.IsNullOrEmpty(e.Key) ? e.Key : char.ToLowerInvariant(e.Key[0]) + e.Key[1..],
            e => e.Value);
}
