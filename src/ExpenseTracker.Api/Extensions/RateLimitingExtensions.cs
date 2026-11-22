using System.Globalization;
using System.Threading.RateLimiting;
using ExpenseTracker.Api.ExceptionHandling;
using ExpenseTracker.Api.Options;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.Api.Extensions;

/// <summary>
/// Configures fixed-window IP rate limiting on authentication endpoints.
/// </summary>
public static class RateLimitingExtensions
{
    public const string AuthPolicyName = "auth";

    public static IServiceCollection AddAuthRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                var rateOptions = context.HttpContext.RequestServices
                    .GetRequiredService<IOptions<RateLimitingOptions>>()
                    .Value;

                var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    : rateOptions.WindowSeconds;

                context.HttpContext.Response.Headers.RetryAfter =
                    retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

                await ProblemDetailsResponseWriter.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    "rate_limited",
                    "Too many requests. Please try again later.",
                    cancellationToken);
            };

            options.AddPolicy(AuthPolicyName, httpContext =>
            {
                var rateOptions = httpContext.RequestServices
                    .GetRequiredService<IOptions<RateLimitingOptions>>()
                    .Value;

                // Partition strictly by TCP RemoteIpAddress; do not trust spoofable forwarded headers in MVP.
                var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateOptions.PermitLimit,
                        Window = TimeSpan.FromSeconds(rateOptions.WindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });

        return services;
    }
}
