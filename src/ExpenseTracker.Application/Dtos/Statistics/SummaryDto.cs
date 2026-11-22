namespace ExpenseTracker.Application.Dtos.Statistics;

/// <summary>
/// Income, expense, net balance, and transaction count over an optional date range.
/// </summary>
public sealed record SummaryDto(
    DateOnly? From,
    DateOnly? To,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal Balance,
    int TransactionCount);
