namespace ExpenseTracker.Application.Dtos.Statistics;

/// <summary>
/// Totals for a single calendar year and month used in period comparisons.
/// </summary>
public sealed record PeriodTotalsDto(
    int Year,
    int Month,
    decimal Income,
    decimal Expense,
    decimal Balance);
