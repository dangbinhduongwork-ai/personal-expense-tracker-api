using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace ExpenseTracker.Api.ExceptionHandling;

/// <summary>
/// Shared helper for emitting standardized RFC 9457 ProblemDetails responses from middleware callbacks.
/// </summary>
internal static class ProblemDetailsResponseWriter
{
    public static string ResolveDefaultCode(int statusCode) =>
        statusCode switch
        {
            StatusCodes.Status400BadRequest => "validation_failed",
            StatusCodes.Status401Unauthorized => "unauthorized",
            StatusCodes.Status403Forbidden => "forbidden",
            StatusCodes.Status404NotFound => "not_found",
            StatusCodes.Status405MethodNotAllowed => "method_not_allowed",
            StatusCodes.Status409Conflict => "conflict",
            StatusCodes.Status415UnsupportedMediaType => "unsupported_media_type",
            StatusCodes.Status429TooManyRequests => "rate_limited",
            _ when statusCode >= 500 => "internal_error",
            _ => "request_failed"
        };

    public static async Task WriteAsync(
        HttpContext httpContext,
        int statusCode,
        string code,
        string detail,
        CancellationToken cancellationToken = default)
    {
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = ReasonPhrases.GetReasonPhrase(statusCode),
            Type = $"https://httpstatuses.io/{statusCode}",
            Detail = detail
        };
        problemDetails.Extensions["code"] = code;
        problemDetails.Extensions["traceId"] = traceId;

        var problemDetailsService = httpContext.RequestServices.GetService<IProblemDetailsService>();
        if (problemDetailsService is not null)
        {
            var wrote = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problemDetails
            });

            if (wrote)
            {
                return;
            }
        }

        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            problemDetails.GetType(),
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);
    }
}
