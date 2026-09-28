using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Offload.Crm.Infrastructure.Persistence;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Offload.Crm.Web.Api;

/// <summary>Adds the list query parameters (paging + filters) and the 401 response to the OpenAPI document (D-25).</summary>
internal sealed class ApiQueryOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<ApiQueryParameters>().FirstOrDefault();
        if (metadata is not null)
        {
            var parameters = new List<(string Name, string Description)>();
            if (metadata.Paged)
            {
                parameters.Add(("search", "case-insensitive contains"));
                parameters.Add(("sort", "column key (see summary)"));
                parameters.Add(("descending", "true | false"));
                parameters.Add(("page", "1-based (default 1)"));
                parameters.Add(("pageSize", "1-100 (default 25)"));
            }

            parameters.AddRange(metadata.Filters);
            foreach (var (name, description) in parameters)
            {
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = name,
                    In = ParameterLocation.Query,
                    Description = description,
                    Schema = new OpenApiSchema { Type = "string" },
                });
            }
        }

        if (context.ApiDescription.RelativePath?.StartsWith("api/", StringComparison.Ordinal) == true)
        {
            operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Not signed in (cookie auth)" });
        }
    }
}

/// <summary><c>/health</c>: can the app reach its database? (D-25)</summary>
internal sealed class DatabaseHealthCheck(IDbContextFactory<AppDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database is not reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("The database is not reachable.", ex);
        }
    }
}
