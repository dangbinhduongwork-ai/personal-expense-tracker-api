using System.Net;
using ExpenseTracker.Application.Dtos.Statistics;
using ExpenseTracker.Infrastructure.Data.Seed;
using ExpenseTracker.IntegrationTests.Infrastructure;
using ExpenseTracker.IntegrationTests.Statistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace ExpenseTracker.IntegrationTests.Security;

[Collection(SqlServerCollection.Name)]
public sealed class StatisticsSecurityTests(SqlServerFixture fixture)
{
    private static readonly DateTimeOffset FixedNow = new(2026, 12, 15, 3, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("/api/statistics/summary")]
    [InlineData("/api/statistics/daily?from=2026-11-01&to=2026-11-03")]
    [InlineData("/api/statistics/monthly?year=2026")]
    [InlineData("/api/statistics/yearly")]
    [InlineData("/api/statistics/categories?type=Expense")]
    [InlineData("/api/statistics/top-categories?type=Expense")]
    [InlineData("/api/statistics/comparison?year=2026&month=11")]
    public async Task SEC01_StatisticsEndpoints_WithoutToken_Return401Unauthorized(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, timeProvider: new FakeTimeProvider(FixedNow));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, ct);
        var problem = await response.ReadProblemDetailsAsync(ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", problem.Code);
    }

    [Fact]
    public async Task SEC14_AllSevenStatisticsEndpoints_ExcludeUserBDataCompletely()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, timeProvider: new FakeTimeProvider(FixedNow));
        using var anonClient = factory.CreateClient();

        var userA = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        var userB = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var clientA = factory.CreateAuthenticatedClient(userA.Auth.AccessToken);
        using var clientB = factory.CreateAuthenticatedClient(userB.Auth.AccessToken);

        await StatisticsEndpointsTests.SeedDatasetDs1Async(clientA, clientB, ct);

        var summaryA = await clientA.GetApiJsonAsync<SummaryDto>("/api/statistics/summary?year=2026&month=11", ct);
        var dailyA = await clientA.GetApiJsonAsync<DailyStatDto[]>("/api/statistics/daily?from=2026-11-02&to=2026-11-02", ct);
        var monthlyA = await clientA.GetApiJsonAsync<MonthlyStatDto[]>("/api/statistics/monthly?year=2026", ct);
        var yearlyA = await clientA.GetApiJsonAsync<YearlyStatDto[]>("/api/statistics/yearly?fromYear=2026&toYear=2026", ct);
        var expenseCategoriesA = await clientA.GetApiJsonAsync<CategoryStatDto[]>("/api/statistics/categories?type=Expense&from=2026-11-01&to=2026-11-30", ct);
        var incomeCategoriesA = await clientA.GetApiJsonAsync<CategoryStatDto[]>("/api/statistics/categories?type=Income&from=2026-11-01&to=2026-11-30", ct);
        var topA = await clientA.GetApiJsonAsync<CategoryStatDto[]>("/api/statistics/top-categories?type=Expense&limit=5&from=2026-11-01&to=2026-11-30", ct);
        var comparisonA = await clientA.GetApiJsonAsync<MonthComparisonDto>("/api/statistics/comparison?year=2026&month=11", ct);

        Assert.Equal(25_000_000.00m, summaryA!.TotalIncome);
        Assert.Equal(2_500_000.00m, summaryA.TotalExpense);
        Assert.Equal(new DailyStatDto(new DateOnly(2026, 11, 2), 0m, 0m, 0m), Assert.Single(dailyA!));
        Assert.Equal(25_000_000.00m, monthlyA![10].Income);
        Assert.Equal(2_500_000.00m, monthlyA[10].Expense);
        var singleYearA = Assert.Single(yearlyA!);
        Assert.Equal(45_000_000.00m, singleYearA.Income);
        Assert.Equal(5_500_000.00m, singleYearA.Expense);
        Assert.Equal(2_500_000.00m, expenseCategoriesA!.Sum(c => c.Total));
        Assert.Equal(25_000_000.00m, incomeCategoriesA!.Sum(c => c.Total));
        Assert.Equal(2_500_000.00m, topA!.Sum(c => c.Total));
        Assert.Equal(25_000_000.00m, comparisonA!.Current.Income);
        Assert.Equal(2_500_000.00m, comparisonA.Current.Expense);
    }

    [Theory]
    [InlineData("/api/statistics/daily?from=2025-01-01&to=2026-01-02", "invalid_date_range")]
    [InlineData("/api/statistics/yearly?fromYear=2000&toYear=2020", "invalid_date_range")]
    [InlineData("/api/statistics/top-categories?type=Expense&limit=21", "validation_failed")]
    [InlineData("/api/statistics/top-categories?type=Expense&limit=0", "validation_failed")]
    [InlineData("/api/statistics/categories?type=1", "validation_failed")]
    [InlineData("/api/statistics/top-categories?type=1", "validation_failed")]
    [InlineData("/api/statistics/summary?year=1999", "validation_failed")]
    [InlineData("/api/statistics/monthly?year=2101", "validation_failed")]
    [InlineData("/api/statistics/comparison?year=2026&month=13", "validation_failed")]
    public async Task SEC21_StatisticsCapsAndStrictEnumBinding_AreEnforced(string path, string expectedCode)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, timeProvider: new FakeTimeProvider(FixedNow));
        using var anonClient = factory.CreateClient();

        var user = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(user.Auth.AccessToken);

        using var response = await client.GetAsync(path, ct);
        var problem = await response.ReadProblemDetailsAsync(ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, problem.Code);
    }

    [Fact]
    public async Task DemoDataSeeder_InProductionEnvironment_NeverSeedsEvenWhenEnabled()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var cleanupDb = fixture.CreateDbContext())
        {
            await cleanupDb.Users
                .Where(u => u.Email == DemoDataSeeder.DemoUserEmail)
                .ExecuteDeleteAsync(ct);
        }

        await using var prodFactory = new ApiFactory(
            fixture.ConnectionString,
            environmentName: "Production",
            timeProvider: new FakeTimeProvider(FixedNow),
            seedDemoData: true);

        using var client = prodFactory.CreateClient();
        using var health = await client.GetAsync("/health", ct);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        await using var verifyDb = fixture.CreateDbContext();
        Assert.False(await verifyDb.Users.AnyAsync(u => u.Email == DemoDataSeeder.DemoUserEmail, ct));

        // Verify Development + Seed:DemoData=true seeds 200 transactions idempotently on real SQL Server.
        await using (var devFactory = new ApiFactory(
            fixture.ConnectionString,
            environmentName: "Development",
            timeProvider: new FakeTimeProvider(FixedNow),
            seedDemoData: true))
        {
            using var devClient = devFactory.CreateClient();
            using var devHealth = await devClient.GetAsync("/health", ct);
            Assert.Equal(HttpStatusCode.OK, devHealth.StatusCode);
        }

        await using (var devFactoryAgain = new ApiFactory(
            fixture.ConnectionString,
            environmentName: "Development",
            timeProvider: new FakeTimeProvider(FixedNow),
            seedDemoData: true))
        {
            using var devClientAgain = devFactoryAgain.CreateClient();
            using var devHealthAgain = await devClientAgain.GetAsync("/health", ct);
            Assert.Equal(HttpStatusCode.OK, devHealthAgain.StatusCode);
        }

        await using var afterDevDb = fixture.CreateDbContext();
        var demoUser = await afterDevDb.Users.SingleAsync(u => u.Email == DemoDataSeeder.DemoUserEmail, ct);
        var demoTxCount = await afterDevDb.Transactions.CountAsync(t => t.UserId == demoUser.Id, ct);
        Assert.Equal(DemoDataSeeder.DemoTransactionCount, demoTxCount);
    }
}
