using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Api.Options;

/// <summary>
/// Configures fixed-window rate limiting on authentication endpoints.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 100000)]
    public int PermitLimit { get; set; } = 10;

    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;
}
