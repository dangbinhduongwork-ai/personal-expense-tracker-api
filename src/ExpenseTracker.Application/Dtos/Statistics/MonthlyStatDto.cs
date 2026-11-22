namespace ExpenseTracker.Application.Dtos.Statistics;

/// <summary>
/// Monthly income, expense, and balance totals for a given calendar month.
/// </summary>
public sealed record MonthlyStatDto(
    int Year,
    int Month,
    decimal Income,
    decimal Expense,
    decimal Balance);
