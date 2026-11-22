using ExpenseTracker.Application.Dtos.Transactions;
using ExpenseTracker.Application.Entities;

namespace ExpenseTracker.Application.Mapping;

/// <summary>
/// Manual mapping helpers for transaction entities.
/// </summary>
public static class TransactionMappings
{
    public static TransactionDto ToDto(this Transaction transaction, string categoryName) =>
        new(
            transaction.Id,
            transaction.Title,
            transaction.Amount,
            transaction.Type,
            transaction.CategoryId,
            categoryName,
            transaction.Description,
            transaction.TransactionDate,
            transaction.CreatedAt,
            transaction.UpdatedAt);
}
