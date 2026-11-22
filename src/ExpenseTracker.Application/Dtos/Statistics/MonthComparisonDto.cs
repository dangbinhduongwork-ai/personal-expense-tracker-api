namespace ExpenseTracker.Application.Dtos.Statistics;

/// <summary>
/// Comparison between a target month and a baseline month, with absolute and percentage changes.
/// </summary>
public sealed record MonthComparisonDto(
    PeriodTotalsDto Current,
    PeriodTotalsDto Previous,
    decimal IncomeChange,
    decimal ExpenseChange,
    decimal BalanceChange,
    decimal? IncomeChangePercent,
    decimal? ExpenseChangePercent);
