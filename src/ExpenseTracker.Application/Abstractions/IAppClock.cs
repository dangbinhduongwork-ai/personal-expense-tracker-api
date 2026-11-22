namespace ExpenseTracker.Application.Abstractions;

/// <summary>
/// Provides UTC timestamps and the application's configured local calendar date ("today").
/// </summary>
public interface IAppClock
{
    TimeZoneInfo AppTimeZone { get; }

    DateTime UtcNow { get; }

    DateOnly Today { get; }
}

