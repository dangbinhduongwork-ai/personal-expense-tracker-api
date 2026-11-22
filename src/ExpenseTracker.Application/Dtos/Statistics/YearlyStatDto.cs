namespace ExpenseTracker.Application.Dtos.Statistics;

/// <summary>
/// Yearly income, expense, and balance totals for a given calendar year.
/// </summary>
public sealed record YearlyStatDto(
    int Year,
    decimal Income,
    decimal Expense,
    decimal Balance);
