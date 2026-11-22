using System.Diagnostics;
using ExpenseTracker.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace ExpenseTracker.Api.ExceptionHandling;

/// <summary>
/// Translates application exceptions and unhandled errors into RFC 9457 ProblemDetails responses
/// with stable machine-readable <c>code</c> and <c>traceId</c> fields.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ProblemDetails problemDetails = CreateProblemDetails(httpContext, exception);
        var statusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";

        var wrote = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });

        // Fallback when client Accept header excludes JSON/*/* so exceptions still emit RFC 9457 ProblemDetails
        // instead of turning 404/400/403 into empty bodies or unhandled ExceptionHandlerMiddleware errors.
        if (!wrote)
        {
            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                problemDetails.GetType(),
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);
        }

        return true;
    }

    private ProblemDetails CreateProblemDetails(HttpContext httpContext, Exception exception)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (exception is ValidationFailedException validationEx)
        {
            var validationProblem = new ValidationProblemDetails(
                new Dictionary<string, string[]>(validationEx.Errors))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = ReasonPhrases.GetReasonPhrase(StatusCodes.Status400BadRequest),
                Type = "https://httpstatuses.io/400",
                Detail = validationEx.Message
            };
            validationProblem.Extensions["code"] = validationEx.Code;
            validationProblem.Extensions["traceId"] = traceId;
            return validationProblem;
        }

        if (exception is AppException appEx)
        {
            logger.LogInformation(
                "Handled application exception {Code} ({StatusCode}) for {Method} {Path} TraceId={TraceId}",
                appEx.Code,
                appEx.StatusCode,
                httpContext.Request.Method,
                httpContext.Request.Path,
                traceId);

            var problem = new ProblemDetails
            {
                Status = appEx.StatusCode,
                Title = ReasonPhrases.GetReasonPhrase(appEx.StatusCode),
                Type = $"https://httpstatuses.io/{appEx.StatusCode}",
                Detail = appEx.Message
            };
            problem.Extensions["code"] = appEx.Code;
            problem.Extensions["traceId"] = traceId;
            return problem;
        }

        logger.LogError(
            exception,
            "Unhandled exception while processing {Method} {Path} TraceId={TraceId}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            traceId);

        var detail = environment.IsDevelopment()
            ? exception.Message
            : "An unexpected error occurred.";

        var internalProblem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = ReasonPhrases.GetReasonPhrase(StatusCodes.Status500InternalServerError),
            Type = "https://httpstatuses.io/500",
            Detail = detail
        };
        internalProblem.Extensions["code"] = "internal_error";
        internalProblem.Extensions["traceId"] = traceId;
        return internalProblem;
    }
}
