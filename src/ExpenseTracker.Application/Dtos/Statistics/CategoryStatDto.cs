using ExpenseTracker.Application.Enums;

namespace ExpenseTracker.Application.Dtos.Statistics;

/// <summary>
/// Aggregated category total, transaction count, and percentage share of the period total for the type.
/// </summary>
public sealed record CategoryStatDto(
    int CategoryId,
    string CategoryName,
    TransactionType Type,
    decimal Total,
    int TransactionCount,
    decimal Percentage);
