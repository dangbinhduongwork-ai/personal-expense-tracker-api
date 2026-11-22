using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Common;
using ExpenseTracker.Application.Dtos.Transactions;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Enums;
using ExpenseTracker.Application.Exceptions;
using ExpenseTracker.Application.Services.Transactions;
using ExpenseTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace ExpenseTracker.UnitTests.Services;

public sealed class TransactionServiceTests
{
    // 2026-12-15 03:00 UTC == 2026-12-15 10:00 Asia/Ho_Chi_Minh (Today = 2026-12-15)
    private static readonly DateTimeOffset FixedNow = new(2026, 12, 15, 3, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(0.001)]
    [InlineData(12.345)]
    [InlineData(1_000_000_000_000.01)]
    public async Task CreateAsync_InvalidAmount_ThrowsValidationFailedOnAmount(double rawAmount)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var service = CreateService(db, userId: 1);

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            service.CreateAsync(new CreateTransactionRequest
            {
                Title = "Invalid Amount",
                Amount = (decimal)rawAmount,
                Type = TransactionType.Expense,
                CategoryId = 1,
                TransactionDate = new DateOnly(2026, 12, 15)
            }, ct));

        Assert.Equal("validation_failed", ex.Code);
        Assert.True(ex.Errors.ContainsKey("amount"));
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(1_000_000_000_000)]
    public async Task CreateAsync_BoundaryValidAmount_Succeeds(double rawAmount)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var service = CreateService(db, userId: 5);
        var amount = (decimal)rawAmount;

        var created = await service.CreateAsync(new CreateTransactionRequest
        {
            Title = "  Boundary Tx  ",
            Amount = amount,
            Type = TransactionType.Expense,
            CategoryId = 1,
            Description = "   ",
            TransactionDate = new DateOnly(2026, 12, 15)
        }, ct);

        Assert.Equal("Boundary Tx", created.Title);
        Assert.Equal(amount, created.Amount);
        Assert.Null(created.Description);

        var saved = await db.Transactions.SingleAsync(t => t.Id == created.Id, ct);
        Assert.Equal(5, saved.UserId);
        Assert.Equal(FixedNow.UtcDateTime, saved.CreatedAt);
        Assert.Equal(FixedNow.UtcDateTime, saved.UpdatedAt);
    }

    [Fact]
    public async Task CreateAsync_InvalidDates_ThrowsValidationFailedAndAllowsMinAndTomorrow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var service = CreateService(db, userId: 1);

        DateOnly[] invalidDates =
        [
            default,
            new DateOnly(1999, 12, 31),
            new DateOnly(2026, 12, 17) // Today is 2026-12-15 -> tomorrow + 1
        ];

        foreach (var badDate in invalidDates)
        {
            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
                service.CreateAsync(new CreateTransactionRequest
                {
                    Title = "Date Check",
                    Amount = 100m,
                    Type = TransactionType.Expense,
                    CategoryId = 1,
                    TransactionDate = badDate
                }, ct));

            Assert.True(ex.Errors.ContainsKey("transactionDate"));
        }

        var minValid = await service.CreateAsync(new CreateTransactionRequest
        {
            Title = "Min Date",
            Amount = 50m,
            Type = TransactionType.Expense,
            CategoryId = 1,
            TransactionDate = new DateOnly(2000, 1, 1)
        }, ct);
        Assert.Equal(new DateOnly(2000, 1, 1), minValid.TransactionDate);

        var tomorrowValid = await service.CreateAsync(new CreateTransactionRequest
        {
            Title = "Tomorrow Date",
            Amount = 50m,
            Type = TransactionType.Income,
            CategoryId = 8,
            TransactionDate = new DateOnly(2026, 12, 16)
        }, ct);
        Assert.Equal(new DateOnly(2026, 12, 16), tomorrowValid.TransactionDate);
    }

    [Fact]
    public async Task CreateAsync_WrongCategoryTypeOrForeignCategory_ThrowsExpectedBusinessRules()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var foreignCategory = new Category
        {
            UserId = 2,
            Name = "User 2 Secret Cat",
            Type = TransactionType.Expense,
            CreatedAt = FixedNow.UtcDateTime,
            UpdatedAt = FixedNow.UtcDateTime
        };
        db.Categories.Add(foreignCategory);
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, userId: 1);

        var mismatch = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CreateAsync(new CreateTransactionRequest
            {
                Title = "Type Mismatch",
                Amount = 100m,
                Type = TransactionType.Income,
                CategoryId = 1, // Food is Expense
                TransactionDate = new DateOnly(2026, 12, 15)
            }, ct));
        Assert.Equal("category_type_mismatch", mismatch.Code);

        var foreign = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CreateAsync(new CreateTransactionRequest
            {
                Title = "Foreign Category",
                Amount = 100m,
                Type = TransactionType.Expense,
                CategoryId = foreignCategory.Id,
                TransactionDate = new DateOnly(2026, 12, 15)
            }, ct));
        Assert.Equal("category_not_found", foreign.Code);
    }

    [Fact]
    public async Task UpdateAndDeleteAsync_ForeignTransactionThrowsNotFoundBeforeCategoryCheck_AndUpdatePreservesCreatedAt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var clock = new FakeTimeProvider(FixedNow);

        var ownTx = CreateTransaction(userId: 1, categoryId: 1, title: "Original", amount: 100m, TransactionType.Expense, new DateOnly(2026, 12, 1), "note");
        var foreignTx = CreateTransaction(userId: 2, categoryId: 1, title: "User 2 Tx", amount: 250m, TransactionType.Expense, new DateOnly(2026, 12, 1), "secret");
        db.Transactions.AddRange(ownTx, foreignTx);
        await db.SaveChangesAsync(ct);

        clock.Advance(TimeSpan.FromHours(5));
        var service = CreateService(db, userId: 1, timeProvider: clock);

        // Even with invalid CategoryId 999999, foreign transaction returns 404 transaction_not_found first.
        var updateForeign = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateAsync(foreignTx.Id, new UpdateTransactionRequest
            {
                Title = "Hijack",
                Amount = 1m,
                Type = TransactionType.Expense,
                CategoryId = 999999,
                TransactionDate = new DateOnly(2026, 12, 15)
            }, ct));
        Assert.Equal("transaction_not_found", updateForeign.Code);

        var deleteForeign = await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(foreignTx.Id, ct));
        Assert.Equal("transaction_not_found", deleteForeign.Code);

        var updated = await service.UpdateAsync(ownTx.Id, new UpdateTransactionRequest
        {
            Title = "Updated Title",
            Amount = 300.50m,
            Type = TransactionType.Income,
            CategoryId = 8, // Salary
            Description = "   ",
            TransactionDate = new DateOnly(2026, 12, 10)
        }, ct);

        Assert.Equal("Updated Title", updated.Title);
        Assert.Equal("Salary", updated.CategoryName);
        Assert.Null(updated.Description);
        Assert.Equal(FixedNow.UtcDateTime, updated.CreatedAt);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, updated.UpdatedAt);
    }

    [Fact]
    public async Task GetAllAsync_FiltersSortsTieBreaksAndPagesDeterministically()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var sameDate = new DateOnly(2026, 12, 10);
        db.Transactions.AddRange(
            CreateTransaction(userId: 1, categoryId: 1, title: "Coffee Alpha", amount: 40m, TransactionType.Expense, sameDate, null),
            CreateTransaction(userId: 1, categoryId: 3, title: "Book Beta", amount: 80m, TransactionType.Expense, sameDate, null),
            CreateTransaction(userId: 1, categoryId: 1, title: "Coffee Gamma", amount: 20m, TransactionType.Expense, sameDate, null),
            CreateTransaction(userId: 1, categoryId: 8, title: "Salary Dec", amount: 5000m, TransactionType.Income, new DateOnly(2026, 12, 1), null),
            CreateTransaction(userId: 2, categoryId: 1, title: "Coffee Foreign", amount: 999m, TransactionType.Expense, sameDate, null));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, userId: 1);

        var page1 = await service.GetAllAsync(new TransactionListQuery
        {
            Type = TransactionType.Expense,
            Page = 1,
            PageSize = 2,
            SortBy = "date",
            SortDir = "desc"
        }, ct);

        var page2 = await service.GetAllAsync(new TransactionListQuery
        {
            Type = TransactionType.Expense,
            Page = 2,
            PageSize = 2,
            SortBy = "date",
            SortDir = "desc"
        }, ct);

        var beyond = await service.GetAllAsync(new TransactionListQuery
        {
            Type = TransactionType.Expense,
            Page = 5,
            PageSize = 2
        }, ct);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.True(page1.Items[0].Id > page1.Items[1].Id);
        Assert.True(page1.Items[1].Id > page2.Items[0].Id);
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.TotalCount);

        var filtered = await service.GetAllAsync(new TransactionListQuery
        {
            CategoryId = 1,
            Search = "  COFFEE ",
            From = new DateOnly(2026, 12, 10),
            To = new DateOnly(2026, 12, 10),
            SortBy = "amount",
            SortDir = "asc"
        }, ct);

        Assert.Equal(["Coffee Gamma", "Coffee Alpha"], filtered.Items.Select(t => t.Title).ToArray());
    }

    [Fact]
    public async Task GetAllAsync_InvalidRangeOrSortOrSearchLength_ThrowsExpectedExceptions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var service = CreateService(db, userId: 1);

        var badRange = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.GetAllAsync(new TransactionListQuery
            {
                From = new DateOnly(2026, 12, 10),
                To = new DateOnly(2026, 12, 1)
            }, ct));
        Assert.Equal("invalid_date_range", badRange.Code);

        var badQuery = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            service.GetAllAsync(new TransactionListQuery
            {
                Page = 0,
                PageSize = 101,
                Search = new string('x', 101),
                SortBy = "userId",
                SortDir = "sideways"
            }, ct));

        Assert.Equal("validation_failed", badQuery.Code);
        Assert.True(badQuery.Errors.ContainsKey("page"));
        Assert.True(badQuery.Errors.ContainsKey("pageSize"));
        Assert.True(badQuery.Errors.ContainsKey("search"));
        Assert.True(badQuery.Errors.ContainsKey("sortBy"));
        Assert.True(badQuery.Errors.ContainsKey("sortDir"));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TransactionUnit_{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static TransactionService CreateService(AppDbContext db, int userId, TimeProvider? timeProvider = null)
    {
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.SetupGet(c => c.UserId).Returns(userId);
        var clock = new AppClock(timeProvider ?? new FakeTimeProvider(FixedNow), AppClock.ResolveTimeZone("Asia/Ho_Chi_Minh"));

        return new TransactionService(db, currentUserMock.Object, clock, NullLogger<TransactionService>.Instance);
    }

    private static Transaction CreateTransaction(
        int userId,
        int categoryId,
        string title,
        decimal amount,
        TransactionType type,
        DateOnly date,
        string? description) =>
        new()
        {
            UserId = userId,
            CategoryId = categoryId,
            Title = title,
            Amount = amount,
            Type = type,
            Description = description,
            TransactionDate = date,
            CreatedAt = FixedNow.UtcDateTime,
            UpdatedAt = FixedNow.UtcDateTime
        };
}
