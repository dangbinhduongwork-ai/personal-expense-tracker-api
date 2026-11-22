using ExpenseTracker.Application.Enums;

namespace ExpenseTracker.Application.Dtos.Transactions;

/// <summary>
/// Public representation of a user's income or expense transaction.
/// </summary>
public sealed record TransactionDto(
    int Id,
    string Title,
    decimal Amount,
    TransactionType Type,
    int CategoryId,
    string CategoryName,
    string? Description,
    DateOnly TransactionDate,
    DateTime CreatedAt,
    DateTime UpdatedAt);
