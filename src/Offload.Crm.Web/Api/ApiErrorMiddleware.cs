using System.Text.Json;

namespace Offload.Crm.Web.Api;

/// <summary>
/// D-20.7 / BUG-008: every <c>/api</c> error is <c>application/problem+json</c>. Bad requests (malformed JSON, wrong
/// types, missing body, unparsable query values) become a readable 400 and are logged as information only; any
/// other exception becomes a 500 without a stack trace.
/// </summary>
public sealed class ApiErrorMiddleware(RequestDelegate next, ILogger<ApiErrorMiddleware> logger, IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
        {
            logger.LogInformation("Bad API request {Method} {Path}: {Message}", context.Request.Method, context.Request.Path, ex.Message);
            await Results.Problem(statusCode: ex.StatusCode, title: Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(ex.StatusCode), detail: Describe(ex)).ExecuteAsync(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // client went away
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            logger.LogError(ex, "Unhandled API error {Method} {Path}", context.Request.Method, context.Request.Path);
            var detail = environment.IsDevelopment() ? ex.Message : "The error has been logged. Please try again later.";
            await Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "An unexpected error occurred.", detail: detail)
                .ExecuteAsync(context);
        }
    }

    internal static string Describe(BadHttpRequestException ex)
    {
        if (ex.InnerException is JsonException json)
        {
            return string.IsNullOrEmpty(json.Path) || json.Path == "$"
                ? "The request body is not valid JSON."
                : $"The request body is not valid JSON or has a value of the wrong type at '{json.Path}'.";
        }

        // "Required parameter ... was not provided from body." / "Implicit body inferred ... but no body was provided."
        if (ex.Message.Contains("body", StringComparison.OrdinalIgnoreCase))
        {
            return "A JSON request body is required.";
        }

        return ex.Message; // e.g. Failed to bind parameter "Nullable<CustomerStatus> status" from "bogus".
    }
}
