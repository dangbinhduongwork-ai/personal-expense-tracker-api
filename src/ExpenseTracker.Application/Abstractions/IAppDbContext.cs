using ExpenseTracker.Application.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Application.Abstractions;

/// <summary>
/// Minimal persistence abstraction exposed by Application services without depending on SQL Server.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<Category> Categories { get; }

    DbSet<Transaction> Transactions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<bool> CanConnectAsync(CancellationToken cancellationToken);

    void ClearChangeTracker();

    Task<int> RevokeAllActiveTokensForUserAsync(
        int userId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken);
}

