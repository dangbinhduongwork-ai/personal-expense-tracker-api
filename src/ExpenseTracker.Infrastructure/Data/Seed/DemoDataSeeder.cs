using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Enums;
using ExpenseTracker.Application.Services.Transactions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.Data.Seed;

/// <summary>
/// Idempotent Development-only seeder inserting one demo user and 200 deterministic transactions
/// when <c>Seed:DemoData=true</c>. Never invoked outside <c>Development</c>.
/// </summary>
public sealed class DemoDataSeeder(
    AppDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    IAppClock clock,
    ILogger<DemoDataSeeder> logger)
{
    public const string DemoUserEmail = "demo@expensetracker.local";
    public const int DemoTransactionCount = 200;

    private static readonly (int CategoryId, TransactionType Type, string TitlePrefix, int MinK, int MaxK)[] Templates =
    [
        (1, TransactionType.Expense, "Meal", 35, 180),
        (2, TransactionType.Expense, "Commute", 15, 120),
        (3, TransactionType.Expense, "Shopping", 100, 900),
        (4, TransactionType.Expense, "Entertainment", 60, 450),
        (5, TransactionType.Expense, "Course & Books", 120, 800),
        (6, TransactionType.Expense, "Pharmacy", 50, 400),
        (7, TransactionType.Expense, "Utility Bill", 150, 1200),
        (8, TransactionType.Income, "Monthly Salary", 15000, 25000),
        (9, TransactionType.Income, "Performance Bonus", 2000, 6000),
        (10, TransactionType.Income, "Freelance Project", 1500, 5000),
        (11, TransactionType.Income, "Dividend", 500, 2500),
        (12, TransactionType.Income, "Other Income", 200, 1200)
    ];

    /// <summary>
    /// Seeds one demo user and 200 sample transactions if the demo user does not already exist.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await dbContext.Users.AnyAsync(u => u.Email == DemoUserEmail, cancellationToken))
        {
            return;
        }

        var utcNow = clock.UtcNow;
        var user = new User
        {
            Email = DemoUserEmail,
            FullName = "Demo User",
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
        // Dev-only sample password intentionally used only when Seed:DemoData=true in Development.
        user.PasswordHash = passwordHasher.HashPassword(user, "DemoPass123!");

        var rng = new Random(20261122);
        var today = clock.Today;
        var transactions = new List<Transaction>(DemoTransactionCount);

        for (var i = 0; i < DemoTransactionCount; i++)
        {
            // Keep income entries weighted realistically (roughly 1 in 6 entries is Income).
            var templateIndex = i % 6 == 0
                ? rng.Next(7, Templates.Length)
                : rng.Next(0, 7);
            var template = Templates[templateIndex];

            var daysAgo = rng.Next(0, 180);
            var date = today.AddDays(-daysAgo);
            if (date < TransactionService.MinTransactionDate)
            {
                date = TransactionService.MinTransactionDate;
            }

            var wholeThousands = rng.Next(template.MinK, template.MaxK + 1);
            var cents = rng.Next(0, 100);
            var amount = wholeThousands * 1000m + cents / 100m;

            transactions.Add(new Transaction
            {
                User = user,
                CategoryId = template.CategoryId,
                Type = template.Type,
                Title = $"{template.TitlePrefix} #{i + 1}",
                Amount = amount,
                Description = i % 3 == 0 ? "Seeded demo sample entry" : null,
                TransactionDate = date,
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            });
        }

        // Single SaveChangesAsync wraps demo user + 200 transactions atomically on SQL Server.
        dbContext.Users.Add(user);
        dbContext.Transactions.AddRange(transactions);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {TransactionCount} Development demo transactions for user {UserId}",
            transactions.Count,
            user.Id);
    }
}
