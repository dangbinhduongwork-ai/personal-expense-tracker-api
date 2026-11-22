using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Common;
using ExpenseTracker.Application.Dtos.Statistics;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Enums;
using ExpenseTracker.Application.Exceptions;
using ExpenseTracker.Application.Services.Statistics;
using ExpenseTracker.Infrastructure.Data;
using ExpenseTracker.Infrastructure.Data.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace ExpenseTracker.UnitTests.Services;

public sealed class StatisticsServiceTests
{
    // 2026-12-15 03:00 UTC == 2026-12-15 10:00 Asia/Ho_Chi_Minh (Today = 2026-12-15)
    private static readonly DateTimeOffset FixedNow = new(2026, 12, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetDailyAsync_ZeroFillsGapsAndEnforces366InclusiveDayLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        db.Transactions.AddRange(
            CreateTx(userId: 1, categoryId: 8, amount: 1000m, TransactionType.Income, new DateOnly(2026, 11, 1)),
            CreateTx(userId: 1, categoryId: 1, amount: 250.25m, TransactionType.Expense, new DateOnly(2026, 11, 1)),
            CreateTx(userId: 1, categoryId: 1, amount: 99.75m, TransactionType.Expense, new DateOnly(2026, 11, 3)),
            CreateTx(userId: 2, categoryId: 1, amount: 99999m, TransactionType.Expense, new DateOnly(2026, 11, 2)));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, userId: 1);

        var daily = await service.GetDailyAsync(
            new DailyStatisticsQuery { From = new DateOnly(2026, 11, 1), To = new DateOnly(2026, 11, 3) },
            ct);

        Assert.Equal(3, daily.Count);
        Assert.Equal(new DailyStatDto(new DateOnly(2026, 11, 1), 1000m, 250.25m, 749.75m), daily[0]);
        Assert.Equal(new DailyStatDto(new DateOnly(2026, 11, 2), 0m, 0m, 0m), daily[1]);
        Assert.Equal(new DailyStatDto(new DateOnly(2026, 11, 3), 0m, 99.75m, -99.75m), daily[2]);

        // Exactly 366 inclusive days (2024 leap year Jan 1..Dec 31) is allowed.
        var leapYearDaily = await service.GetDailyAsync(
            new DailyStatisticsQuery { From = new DateOnly(2024, 1, 1), To = new DateOnly(2024, 12, 31) },
            ct);
        Assert.Equal(366, leapYearDaily.Count);

        var overLimit = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.GetDailyAsync(
                new DailyStatisticsQuery { From = new DateOnly(2024, 1, 1), To = new DateOnly(2025, 1, 1) },
                ct));
        Assert.Equal("invalid_date_range", overLimit.Code);

        var reversed = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.GetDailyAsync(
                new DailyStatisticsQuery { From = new DateOnly(2026, 11, 5), To = new DateOnly(2026, 11, 1) },
                ct));
        Assert.Equal("invalid_date_range", reversed.Code);
    }

    [Fact]
    public async Task GetMonthlyAndYearlyAsync_ZeroFillAndEnforceDefaultAnd20YearSpan()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        db.Transactions.AddRange(
            CreateTx(userId: 1, categoryId: 8, amount: 500m, TransactionType.Income, new DateOnly(2026, 4, 10)),
            CreateTx(userId: 1, categoryId: 1, amount: 120m, TransactionType.Expense, new DateOnly(2026, 4, 11)));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, userId: 1);

        var monthly = await service.GetMonthlyAsync(new MonthlyStatisticsQuery { Year = 2026 }, ct);
        Assert.Equal(12, monthly.Count);
        Assert.Equal(new MonthlyStatDto(2026, 1, 0m, 0m, 0m), monthly[0]);
        Assert.Equal(new MonthlyStatDto(2026, 4, 500m, 120m, 380m), monthly[3]);
        Assert.Equal(new MonthlyStatDto(2026, 12, 0m, 0m, 0m), monthly[11]);

        var defaultYearly = await service.GetYearlyAsync(new YearlyStatisticsQuery(), ct);
        Assert.Equal([2022, 2023, 2024, 2025, 2026], defaultYearly.Select(y => y.Year).ToArray());
        Assert.Equal(new YearlyStatDto(2026, 500m, 120m, 380m), defaultYearly.Last());

        var span20 = await service.GetYearlyAsync(new YearlyStatisticsQuery { FromYear = 2007, ToYear = 2026 }, ct);
        Assert.Equal(20, span20.Count);

        var span21 = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.GetYearlyAsync(new YearlyStatisticsQuery { FromYear = 2006, ToYear = 2026 }, ct));
        Assert.Equal("invalid_date_range", span21.Code);
    }

    [Fact]
    public async Task GetByCategoryAndTopCategories_UseAwayFromZeroRoundingAndFullPeriodShare()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        // Totals sum to 800m: custom cat = 1m (0.125% -> 0.13% AwayFromZero, vs 0.12% ToEven);
        // Food = 500m (62.50%), Bills = 299m (37.375% -> 37.38% AwayFromZero).
        var customCat = new Category
        {
            UserId = 1,
            Name = "Micro Fee",
            Type = TransactionType.Expense,
            CreatedAt = FixedNow.UtcDateTime,
            UpdatedAt = FixedNow.UtcDateTime
        };
        db.Categories.Add(customCat);
        await db.SaveChangesAsync(ct);

        db.Transactions.AddRange(
            CreateTx(userId: 1, categoryId: 1, amount: 500m, TransactionType.Expense, new DateOnly(2026, 11, 1)),
            CreateTx(userId: 1, categoryId: 7, amount: 299m, TransactionType.Expense, new DateOnly(2026, 11, 2)),
            CreateTx(userId: 1, categoryId: customCat.Id, amount: 1m, TransactionType.Expense, new DateOnly(2026, 11, 3)));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, userId: 1);

        var allCats = await service.GetByCategoryAsync(
            new CategoryStatisticsQuery { Type = TransactionType.Expense },
            ct);

        Assert.Equal(3, allCats.Count);
        Assert.Equal(62.50m, allCats[0].Percentage);
        Assert.Equal(37.38m, allCats[1].Percentage);
        Assert.Equal(0.13m, allCats[2].Percentage);

        var topOne = await service.GetTopCategoriesAsync(
            new TopCategoriesQuery { Type = TransactionType.Expense, Limit = 1 },
            ct);

        var topItem = Assert.Single(topOne);
        Assert.Equal("Food", topItem.CategoryName);
        Assert.Equal(62.50m, topItem.Percentage);

        Assert.Equal(0.13m, StatisticsService.CalculatePercent(1m, 800m));
    }

    [Fact]
    public async Task GetComparisonAsync_HandlesYearBoundaryZeroPreviousAndValidationErrors()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        db.Transactions.Add(CreateTx(userId: 1, categoryId: 8, amount: 1000m, TransactionType.Income, new DateOnly(2026, 1, 5)));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, userId: 1);

        var janComparison = await service.GetComparisonAsync(
            new ComparisonQuery { Year = 2026, Month = 1 },
            ct);

        Assert.Equal(new PeriodTotalsDto(2025, 12, 0m, 0m, 0m), janComparison.Previous);
        Assert.Equal(1000m, janComparison.IncomeChange);
        Assert.Null(janComparison.IncomeChangePercent);
        Assert.Null(janComparison.ExpenseChangePercent);

        var partialCompare = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            service.GetComparisonAsync(new ComparisonQuery { Year = 2026, Month = 2, CompareToYear = 2026 }, ct));
        Assert.True(partialCompare.Errors.ContainsKey("compareToMonth"));

        var monthWithoutYear = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            service.GetSummaryAsync(new SummaryQuery { Month = 11 }, ct));
        Assert.True(monthWithoutYear.Errors.ContainsKey("year"));

        var badLimit = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            service.GetTopCategoriesAsync(new TopCategoriesQuery { Type = TransactionType.Expense, Limit = 21 }, ct));
        Assert.True(badLimit.Errors.ContainsKey("limit"));
    }

    [Fact]
    public async Task DemoDataSeeder_SeedsOnceIdempotentlyWithValidCategoriesAndAmounts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var clock = new AppClock(new FakeTimeProvider(FixedNow), AppClock.ResolveTimeZone("Asia/Ho_Chi_Minh"));
        var seeder = new DemoDataSeeder(
            db,
            new PasswordHasher<User>(),
            clock,
            NullLogger<DemoDataSeeder>.Instance);

        await seeder.SeedAsync(ct);
        await seeder.SeedAsync(ct);

        var demoUser = await db.Users.SingleAsync(u => u.Email == DemoDataSeeder.DemoUserEmail, ct);
        var demoTransactions = await db.Transactions.Where(t => t.UserId == demoUser.Id).ToListAsync(ct);

        Assert.Equal(DemoDataSeeder.DemoTransactionCount, demoTransactions.Count);
        Assert.All(demoTransactions, t => Assert.True(t.Amount > 0m));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"StatisticsUnit_{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static StatisticsService CreateService(AppDbContext db, int userId, TimeProvider? timeProvider = null)
    {
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.SetupGet(c => c.UserId).Returns(userId);
        var clock = new AppClock(timeProvider ?? new FakeTimeProvider(FixedNow), AppClock.ResolveTimeZone("Asia/Ho_Chi_Minh"));

        return new StatisticsService(db, currentUserMock.Object, clock);
    }

    private static Transaction CreateTx(
        int userId,
        int categoryId,
        decimal amount,
        TransactionType type,
        DateOnly date) =>
        new()
        {
            UserId = userId,
            CategoryId = categoryId,
            Title = "Stat Unit Row",
            Amount = amount,
            Type = type,
            TransactionDate = date,
            CreatedAt = FixedNow.UtcDateTime,
            UpdatedAt = FixedNow.UtcDateTime
        };
}
