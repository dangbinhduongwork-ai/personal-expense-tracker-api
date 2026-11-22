using System.Net;
using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Enums;
using ExpenseTracker.IntegrationTests.Infrastructure;
using ExpenseTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExpenseTracker.IntegrationTests.Smoke;

[Collection(SqlServerCollection.Name)]
public sealed class StartupAndHealthSmokeTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task HealthEndpoint_WhenDatabaseIsReachable_Returns200HealthyWithNoSniffHeader()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body);
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var headerValues));
        Assert.Contains("nosniff", headerValues);
    }

    [Fact]
    public async Task Migrations_OnFreshDatabase_CreateTables12SeedRowsAndContinueIdentityAt13()
    {
        var ct = TestContext.Current.CancellationToken;
        var freshDbConnection = fixture.CreateDatabaseConnectionString($"ExpenseTrackerFresh_{Guid.NewGuid():N}");
        await using var factory = new ApiFactory(freshDbConnection);
        using var _ = factory.CreateClient();

        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(freshDbConnection)
            .Options;

        await using var db = new AppDbContext(dbOptions);
        try
        {
            var categories = await db.Categories
                .AsNoTracking()
                .OrderBy(c => c.Id)
                .ToListAsync(ct);

            Assert.Equal(12, categories.Count);
            Assert.Equal("Food", categories[0].Name);
            Assert.Equal("Other Income", categories[11].Name);
            Assert.All(categories, category => Assert.Null(category.UserId));

            var user = CreateUser();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            var personalCategory = new Category
            {
                UserId = user.Id,
                Name = "Personal Coffee",
                Type = TransactionType.Expense,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Categories.Add(personalCategory);
            await db.SaveChangesAsync(ct);

            Assert.Equal(13, personalCategory.Id);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DatabaseConstraints_OnRealSqlServer_EnforceCheckUniqueIndexesFkRestrictAndUserCascade()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateDbContext();

        var owner = CreateUser();
        db.Users.Add(owner);
        await db.SaveChangesAsync(ct);

        // 1. Unique email (case-insensitive under SQL Server CI collation)
        await using (var dupEmailDb = fixture.CreateDbContext())
        {
            dupEmailDb.Users.Add(new User
            {
                Email = owner.Email.ToUpperInvariant(),
                PasswordHash = "hash",
                FullName = "Duplicate",
                CreatedAt = owner.CreatedAt,
                UpdatedAt = owner.UpdatedAt
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => dupEmailDb.SaveChangesAsync(ct));
        }

        // 2. Unfiltered unique index prevents duplicate system category (UserId = null)
        await using (var dupSystemCategoryDb = fixture.CreateDbContext())
        {
            dupSystemCategoryDb.Categories.Add(new Category
            {
                UserId = null,
                Name = "food",
                Type = TransactionType.Expense,
                CreatedAt = owner.CreatedAt,
                UpdatedAt = owner.UpdatedAt
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => dupSystemCategoryDb.SaveChangesAsync(ct));
        }

        // 3. Personal category creation + duplicate personal category check
        var personalCategory = new Category
        {
            UserId = owner.Id,
            Name = "Gym",
            Type = TransactionType.Expense,
            CreatedAt = owner.CreatedAt,
            UpdatedAt = owner.UpdatedAt
        };
        db.Categories.Add(personalCategory);
        await db.SaveChangesAsync(ct);

        await using (var dupPersonalDb = fixture.CreateDbContext())
        {
            dupPersonalDb.Categories.Add(new Category
            {
                UserId = owner.Id,
                Name = "gym",
                Type = TransactionType.Expense,
                CreatedAt = owner.CreatedAt,
                UpdatedAt = owner.UpdatedAt
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => dupPersonalDb.SaveChangesAsync(ct));
        }

        // 4. Amount CHECK constraint (> 0) rejects zero and negative amounts
        await using (var zeroAmountDb = fixture.CreateDbContext())
        {
            zeroAmountDb.Transactions.Add(new Transaction
            {
                UserId = owner.Id,
                CategoryId = personalCategory.Id,
                Title = "Invalid Zero",
                Amount = 0m,
                Type = TransactionType.Expense,
                TransactionDate = new DateOnly(2026, 11, 22),
                CreatedAt = owner.CreatedAt,
                UpdatedAt = owner.UpdatedAt
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => zeroAmountDb.SaveChangesAsync(ct));
        }

        await using (var negativeAmountDb = fixture.CreateDbContext())
        {
            negativeAmountDb.Transactions.Add(new Transaction
            {
                UserId = owner.Id,
                CategoryId = personalCategory.Id,
                Title = "Invalid Negative",
                Amount = -1.00m,
                Type = TransactionType.Expense,
                TransactionDate = new DateOnly(2026, 11, 22),
                CreatedAt = owner.CreatedAt,
                UpdatedAt = owner.UpdatedAt
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => negativeAmountDb.SaveChangesAsync(ct));
        }

        // 5. Valid transaction succeeds, then deleting used category fails (FK Restrict / NO ACTION)
        var transaction = new Transaction
        {
            UserId = owner.Id,
            CategoryId = personalCategory.Id,
            Title = "Monthly Membership",
            Amount = 500000.00m,
            Type = TransactionType.Expense,
            TransactionDate = new DateOnly(2026, 11, 22),
            CreatedAt = owner.CreatedAt,
            UpdatedAt = owner.UpdatedAt
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(ct);

        await using (var deleteCategoryDb = fixture.CreateDbContext())
        {
            var categoryRef = await deleteCategoryDb.Categories.FirstAsync(c => c.Id == personalCategory.Id, ct);
            deleteCategoryDb.Categories.Remove(categoryRef);

            await Assert.ThrowsAsync<DbUpdateException>(() => deleteCategoryDb.SaveChangesAsync(ct));
        }

        // 6. Deleting owner user cascades cleanly across personal categories and transactions without Msg 1785/547
        await using (var deleteUserDb = fixture.CreateDbContext())
        {
            var userRef = await deleteUserDb.Users.FirstAsync(u => u.Id == owner.Id, ct);
            deleteUserDb.Users.Remove(userRef);
            await deleteUserDb.SaveChangesAsync(ct);

            Assert.False(await deleteUserDb.Transactions.AnyAsync(t => t.UserId == owner.Id, ct));
            Assert.False(await deleteUserDb.Categories.AnyAsync(c => c.UserId == owner.Id, ct));
        }
    }

    [Fact]
    public async Task OpenApi_WhenEnabled_Returns200ForJsonAndSwaggerUi()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, openApiEnableUi: true);
        using var client = factory.CreateClient();

        using var openApiResponse = await client.GetAsync("/openapi/v1.json", ct);
        var openApiBody = await openApiResponse.Content.ReadAsStringAsync(ct);
        using var swaggerResponse = await client.GetAsync("/swagger/index.html", ct);

        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Contains("\"openapi\"", openApiBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, swaggerResponse.StatusCode);
    }

    [Fact]
    public async Task OpenApi_WhenDisabled_Returns404ForSwaggerAndOpenApiJson()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(
            fixture.ConnectionString,
            openApiEnableUi: false,
            environmentName: "Production");
        using var client = factory.CreateClient();

        using var openApiResponse = await client.GetAsync("/openapi/v1.json", ct);
        using var swaggerResponse = await client.GetAsync("/swagger/index.html", ct);

        Assert.Equal(HttpStatusCode.NotFound, openApiResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, swaggerResponse.StatusCode);
    }

    [Fact]
    public async Task UnhandledException_InProduction_Returns500ProblemDetailsWithoutInternalLeak()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(
            fixture.ConnectionString,
            openApiEnableUi: false,
            environmentName: "Production",
            configureExtraServices: services =>
            {
                services.AddControllers().AddApplicationPart(typeof(TestFaultController).Assembly);
            });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/test-diagnostics/unhandled", ct);
        var rawBody = await response.Content.ReadAsStringAsync(ct);
        var problem = await response.ReadProblemDetailsAsync(ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(500, problem.Status);
        Assert.Equal("internal_error", problem.Code);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
        Assert.Equal("An unexpected error occurred.", problem.Detail);
        Assert.DoesNotContain("Sensitive internal SQL exception text", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", rawBody, StringComparison.Ordinal);
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var headerValues));
        Assert.Contains("nosniff", headerValues);
    }

    [Fact]
    public async Task HealthEndpoint_WhenDatabaseIsUnreachable_Returns503UnhealthyWithoutLeak()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(
            fixture.ConnectionString,
            environmentName: "Production",
            migrateOnStartup: false,
            configureExtraServices: services =>
            {
                services.RemoveAll<IAppDbContext>();
                services.AddScoped<IAppDbContext, DisconnectedAppDbContext>();
            });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body);
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var headerValues));
        Assert.Contains("nosniff", headerValues);
    }

    [Fact]
    public async Task AppClock_FromApiServiceProvider_ResolvesConfiguredTimeZone()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var clock = scope.ServiceProvider.GetRequiredService<IAppClock>();

        Assert.Equal(TimeSpan.FromHours(7), clock.AppTimeZone.BaseUtcOffset);
    }

    private static User CreateUser()
    {
        var utcNow = new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc);
        return new User
        {
            Email = AuthHelper.CreateUniqueEmail("smoke"),
            PasswordHash = "PBKDF2_HASH_PLACEHOLDER",
            FullName = "Smoke Test User",
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    private sealed class DisconnectedAppDbContext : IAppDbContext
    {
        public DbSet<User> Users => throw new NotSupportedException();

        public DbSet<RefreshToken> RefreshTokens => throw new NotSupportedException();

        public DbSet<Category> Categories => throw new NotSupportedException();

        public DbSet<Transaction> Transactions => throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> CanConnectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public void ClearChangeTracker()
        {
        }

        public Task<int> RevokeAllActiveTokensForUserAsync(
            int userId,
            DateTime revokedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
