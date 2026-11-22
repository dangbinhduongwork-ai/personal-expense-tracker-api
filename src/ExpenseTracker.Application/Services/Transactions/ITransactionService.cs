using ExpenseTracker.Application.Dtos.Common;
using ExpenseTracker.Application.Dtos.Transactions;

namespace ExpenseTracker.Application.Services.Transactions;

/// <summary>
/// Application service contract for caller-scoped income and expense transaction CRUD and queries.
/// </summary>
public interface ITransactionService
{
    /// <summary>Lists the caller's transactions with optional filters, search, sorting, and paging.</summary>
    Task<PagedResult<TransactionDto>> GetAllAsync(TransactionListQuery query, CancellationToken cancellationToken);

    /// <summary>Gets a single transaction owned by the caller.</summary>
    Task<TransactionDto> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Creates a new transaction for the caller using an allowed matching category.</summary>
    Task<TransactionDto> CreateAsync(CreateTransactionRequest request, CancellationToken cancellationToken);

    /// <summary>Replaces an existing transaction owned by the caller.</summary>
    Task<TransactionDto> UpdateAsync(int id, UpdateTransactionRequest request, CancellationToken cancellationToken);

    /// <summary>Hard-deletes a transaction owned by the caller.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
