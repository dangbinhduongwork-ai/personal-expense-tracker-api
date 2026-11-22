namespace ExpenseTracker.Application.Dtos.Statistics;

/// <summary>
/// Daily income, expense, and net totals for a single calendar date.
/// </summary>
public sealed record DailyStatDto(
    DateOnly Date,
    decimal Income,
    decimal Expense,
    decimal Net);
