using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Common;
using ExpenseTracker.Application.Dtos.Categories;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Enums;
using ExpenseTracker.Application.Exceptions;
using ExpenseTracker.Application.Services.Categories;
using ExpenseTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace ExpenseTracker.UnitTests.Services;

public sealed class CategoryServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 12, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAllAsync_ReturnsSystemFirstThenOwnSortedByNameExcludingForeignUsers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        db.Categories.AddRange(
            CreateCategory(userId: 1, name: "Zebra Own", type: TransactionType.Expense),
            CreateCategory(userId: 1, name: "Alpha Own", type: TransactionType.Income),
            CreateCategory(userId: 2, name: "Foreign Hidden", type: TransactionType.Expense));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, userId: 1);
        var result = await service.GetAllAsync(type: null, ct);

        Assert.Equal(14, result.Count);
        Assert.All(result.Take(12), c => Assert.True(c.IsSystem));
        Assert.All(result.Skip(12), c => Assert.False(c.IsSystem));
        Assert.Equal(["Alpha Own", "Zebra Own"], result.Skip(12).Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task GetAllAsync_WithTypeFilter_ReturnsMatchingTypeOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        db.Categories.Add(CreateCategory(userId: 1, name: "Side Gig", type: TransactionType.Income));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, userId: 1);
        var result = await service.GetAllAsync(TransactionType.Income, ct);

        Assert.Equal(6, result.Count);
        Assert.Contains(result, c => c.Name == "Side Gig" && !c.IsSystem);
    }

    [Fact]
    public async Task GetByIdAsync_ForeignPersonalCategory_ThrowsNotFoundCategoryNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var foreign = CreateCategory(userId: 2, name: "Secret", type: TransactionType.Expense);
        db.Categories.Add(foreign);
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, userId: 1);
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(foreign.Id, ct));

        Assert.Equal("category_not_found", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_ValidPersonalCategory_TrimsNameAndSetsOwnerAndTimestamps()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var service = CreateService(db, userId: 7);

        var created = await service.CreateAsync(new CreateCategoryRequest
        {
            Name = "  Coffee Shops  ",
            Type = TransactionType.Expense
        }, ct);

        Assert.Equal("Coffee Shops", created.Name);
        Assert.Equal(TransactionType.Expense, created.Type);
        Assert.False(created.IsSystem);
        Assert.Equal(FixedNow.UtcDateTime, created.CreatedAt);

        var saved = await db.Categories.SingleAsync(c => c.Id == created.Id, ct);
        Assert.Equal(7, saved.UserId);
    }

    [Fact]
    public async Task CreateAsync_DuplicateOwnOrSystemNameSameType_ThrowsConflictCategoryNameExists()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        db.Categories.Add(CreateCategory(userId: 1, name: "Pets", type: TransactionType.Expense));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, userId: 1);

        var dupOwn = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new CreateCategoryRequest { Name = "  pets ", Type = TransactionType.Expense }, ct));
        Assert.Equal("category_name_exists", dupOwn.Code);

        var dupSystem = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new CreateCategoryRequest { Name = "FOOD", Type = TransactionType.Expense }, ct));
        Assert.Equal("category_name_exists", dupSystem.Code);
    }

    [Fact]
    public async Task CreateAsync_SameNameAsSystemCategoryOppositeType_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var service = CreateService(db, userId: 1);
        var created = await service.CreateAsync(new CreateCategoryRequest
        {
            Name = "Food",
            Type = TransactionType.Income
        }, ct);

        Assert.Equal(TransactionType.Income, created.Type);
        Assert.False(created.IsSystem);
    }

    [Fact]
    public async Task UpdateAsync_SystemCategoryWithDuplicateName_ThrowsForbiddenBeforeConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var service = CreateService(db, userId: 1);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.UpdateAsync(1, new UpdateCategoryRequest { Name = "Bills", Type = TransactionType.Expense }, ct));

        Assert.Equal("system_category_readonly", ex.Code);
    }

    [Fact]
    public async Task UpdateAsync_KeepSameNameWhileInUse_SucceedsSelfExcluded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var clock = new FakeTimeProvider(FixedNow);
        var cat = CreateCategory(userId: 1, name: "Subscriptions", type: TransactionType.Expense);
        db.Categories.Add(cat);
        await db.SaveChangesAsync(ct);

        db.Transactions.Add(CreateTransaction(userId: 1, categoryId: cat.Id, type: TransactionType.Expense));
        await db.SaveChangesAsync(ct);

        clock.Advance(TimeSpan.FromHours(2));
        var service = CreateService(db, userId: 1, timeProvider: clock);

        var updated = await service.UpdateAsync(cat.Id, new UpdateCategoryRequest
        {
            Name = "subscriptions",
            Type = TransactionType.Expense
        }, ct);

        Assert.Equal("subscriptions", updated.Name);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, (await db.Categories.SingleAsync(c => c.Id == cat.Id, ct)).UpdatedAt);
    }

    [Fact]
    public async Task UpdateAsync_ChangeTypeWhileUsedByTransactions_ThrowsConflictCategoryInUse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var cat = CreateCategory(userId: 1, name: "Side Hustle", type: TransactionType.Income);
        db.Categories.Add(cat);
        await db.SaveChangesAsync(ct);

        db.Transactions.Add(CreateTransaction(userId: 1, categoryId: cat.Id, type: TransactionType.Income));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, userId: 1);
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateAsync(cat.Id, new UpdateCategoryRequest { Name = "Side Hustle", Type = TransactionType.Expense }, ct));

        Assert.Equal("category_in_use", ex.Code);
        Assert.Contains("1 transaction(s)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteAsync_UsedUnusedSystemAndForeign_MatchesRulesExactly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var usedCat = CreateCategory(userId: 1, name: "Used", type: TransactionType.Expense);
        var unusedCat = CreateCategory(userId: 1, name: "Unused", type: TransactionType.Expense);
        var foreignCat = CreateCategory(userId: 2, name: "Foreign", type: TransactionType.Expense);
        db.Categories.AddRange(usedCat, unusedCat, foreignCat);
        await db.SaveChangesAsync(ct);

        db.Transactions.AddRange(
            CreateTransaction(userId: 1, categoryId: usedCat.Id, type: TransactionType.Expense),
            CreateTransaction(userId: 1, categoryId: usedCat.Id, type: TransactionType.Expense));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, userId: 1);

        var systemEx = await Assert.ThrowsAsync<ForbiddenException>(() => service.DeleteAsync(1, ct));
        Assert.Equal("system_category_readonly", systemEx.Code);

        var foreignEx = await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(foreignCat.Id, ct));
        Assert.Equal("category_not_found", foreignEx.Code);

        var usedEx = await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(usedCat.Id, ct));
        Assert.Equal("category_in_use", usedEx.Code);
        Assert.Contains("2 transaction(s)", usedEx.Message, StringComparison.Ordinal);

        await service.DeleteAsync(unusedCat.Id, ct);
        Assert.False(await db.Categories.AnyAsync(c => c.Id == unusedCat.Id, ct));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"CategoryUnit_{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static CategoryService CreateService(AppDbContext db, int userId, TimeProvider? timeProvider = null)
    {
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.SetupGet(c => c.UserId).Returns(userId);
        var clock = new AppClock(timeProvider ?? new FakeTimeProvider(FixedNow), AppClock.ResolveTimeZone("Asia/Ho_Chi_Minh"));

        return new CategoryService(db, currentUserMock.Object, clock, NullLogger<CategoryService>.Instance);
    }

    private static Category CreateCategory(int userId, string name, TransactionType type) =>
        new()
        {
            UserId = userId,
            Name = name,
            Type = type,
            CreatedAt = FixedNow.UtcDateTime,
            UpdatedAt = FixedNow.UtcDateTime
        };

    private static Transaction CreateTransaction(int userId, int categoryId, TransactionType type) =>
        new()
        {
            UserId = userId,
            CategoryId = categoryId,
            Title = "Test Tx",
            Amount = 100m,
            Type = type,
            TransactionDate = new DateOnly(2026, 12, 1),
            CreatedAt = FixedNow.UtcDateTime,
            UpdatedAt = FixedNow.UtcDateTime
        };
}
