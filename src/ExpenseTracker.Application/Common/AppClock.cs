using ExpenseTracker.Application.Abstractions;

namespace ExpenseTracker.Application.Common;

/// <summary>
/// Implements <see cref="IAppClock"/> using the injected <see cref="TimeProvider"/> and configured time zone.
/// </summary>
public sealed class AppClock(TimeProvider timeProvider, TimeZoneInfo appTimeZone) : IAppClock
{
    public TimeZoneInfo AppTimeZone { get; } = appTimeZone;

    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public DateOnly Today
    {
        get
        {
            var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(UtcNow, AppTimeZone);
            return DateOnly.FromDateTime(localDateTime);
        }
    }

    /// <summary>
    /// Resolves <paramref name="configuredTimeZoneId"/> on both Linux (IANA) and Windows,
    /// falling back from "Asia/Ho_Chi_Minh" to "SE Asia Standard Time" when needed.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string configuredTimeZoneId)
    {
        if (string.IsNullOrWhiteSpace(configuredTimeZoneId))
        {
            throw new InvalidOperationException("App:TimeZoneId must be configured.");
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(configuredTimeZoneId, out var timeZone))
        {
            return timeZone;
        }

        if (string.Equals(configuredTimeZoneId, "Asia/Ho_Chi_Minh", StringComparison.OrdinalIgnoreCase) &&
            TimeZoneInfo.TryFindSystemTimeZoneById("SE Asia Standard Time", out var windowsFallback))
        {
            return windowsFallback;
        }

        throw new InvalidOperationException($"Time zone '{configuredTimeZoneId}' could not be resolved on this host.");
    }
}

