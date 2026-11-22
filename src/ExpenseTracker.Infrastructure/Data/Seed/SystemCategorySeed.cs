using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Enums;

namespace ExpenseTracker.Infrastructure.Data.Seed;

/// <summary>
/// Deterministic system categories seeded via EF Core <c>HasData</c> (fixed Ids 1–12 and fixed UTC timestamps).
/// </summary>
internal static class SystemCategorySeed
{
    private static readonly DateTime SeedTimestampUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static Category[] GetSystemCategories() =>
    [
        Create(1, "Food", TransactionType.Expense),
        Create(2, "Transportation", TransactionType.Expense),
        Create(3, "Shopping", TransactionType.Expense),
        Create(4, "Entertainment", TransactionType.Expense),
        Create(5, "Education", TransactionType.Expense),
        Create(6, "Healthcare", TransactionType.Expense),
        Create(7, "Bills", TransactionType.Expense),
        Create(8, "Salary", TransactionType.Income),
        Create(9, "Bonus", TransactionType.Income),
        Create(10, "Freelance", TransactionType.Income),
        Create(11, "Investment", TransactionType.Income),
        Create(12, "Other Income", TransactionType.Income)
    ];

    private static Category Create(int id, string name, TransactionType type) =>
        new()
        {
            Id = id,
            UserId = null,
            Name = name,
            Type = type,
            CreatedAt = SeedTimestampUtc,
            UpdatedAt = SeedTimestampUtc
        };
}

