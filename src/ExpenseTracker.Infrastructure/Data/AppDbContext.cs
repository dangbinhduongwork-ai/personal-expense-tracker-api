using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Data;

/// <summary>
/// Entity Framework Core database context for the Personal Expense Tracker schema.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public Task<bool> CanConnectAsync(CancellationToken cancellationToken) =>
        Database.CanConnectAsync(cancellationToken);

    public void ClearChangeTracker() =>
        ChangeTracker.Clear();

    public async Task<int> RevokeAllActiveTokensForUserAsync(
        int userId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken)
    {
        if (Database.IsRelational())
        {
            return await RefreshTokens
                .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(rt => rt.RevokedAt, revokedAtUtc),
                    cancellationToken);
        }

        // EF InMemory does not support ExecuteUpdateAsync; fall back to tracked updates in unit tests.
        var activeTokens = await RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = revokedAtUtc;
        }

        return await SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

