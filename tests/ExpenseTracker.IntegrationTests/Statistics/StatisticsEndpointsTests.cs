using System.Net;
using ExpenseTracker.Application.Dtos.Statistics;
using ExpenseTracker.Application.Enums;
using ExpenseTracker.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace ExpenseTracker.IntegrationTests.Statistics;

[Collection(SqlServerCollection.Name)]
public sealed class StatisticsEndpointsTests(SqlServerFixture fixture)
{
    private static readonly DateTimeOffset FixedNow = new(2026, 12, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AllSevenEndpoints_WithDatasetDs1AndUserBNoise_MatchSpecExactly()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeClock = new FakeTimeProvider(FixedNow);
        await using var factory = new ApiFactory(fixture.ConnectionString, timeProvider: fakeClock);
        using var anonClient = factory.CreateClient();

        var userA = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        var userB = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var clientA = factory.CreateAuthenticatedClient(userA.Auth.AccessToken);
        using var clientB = factory.CreateAuthenticatedClient(userB.Auth.AccessToken);

        await SeedDatasetDs1Async(clientA, clientB, ct);

        // 1. Summary: Nov, Oct, All-time, Whole-year, Empty Sep
        var nov = await clientA.GetApiJsonAsync<SummaryDto>("/api/statistics/summary?year=2026&month=11", ct);
        Assert.Equal(new SummaryDto(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 25_000_000.00m, 2_500_000.00m, 22_500_000.00m, 6), nov);

        var oct = await clientA.GetApiJsonAsync<SummaryDto>("/api/statistics/summary?year=2026&month=10", ct);
        Assert.Equal(new SummaryDto(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 20_000_000.00m, 3_000_000.00m, 17_000_000.00m, 3), oct);

        var allTime = await clientA.GetApiJsonAsync<SummaryDto>("/api/statistics/summary", ct);
        Assert.Equal(new SummaryDto(null, null, 45_000_000.00m, 5_500_000.00m, 39_500_000.00m, 9), allTime);

        var year2026 = await clientA.GetApiJsonAsync<SummaryDto>("/api/statistics/summary?year=2026", ct);
        Assert.Equal(new SummaryDto(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 45_000_000.00m, 5_500_000.00m, 39_500_000.00m, 9), year2026);

        var emptySep = await clientA.GetApiJsonAsync<SummaryDto>("/api/statistics/summary?year=2026&month=9", ct);
        Assert.Equal(new SummaryDto(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 0m, 0m, 0m, 0), emptySep);

        // 2. Daily: 2026-11-01..2026-11-03 (zero-filled 11-02)
        var daily = await clientA.GetApiJsonAsync<DailyStatDto[]>("/api/statistics/daily?from=2026-11-01&to=2026-11-03", ct);
        Assert.NotNull(daily);
        Assert.Equal(
        [
            new DailyStatDto(new DateOnly(2026, 11, 1), 22_000_000.00m, 150_000.50m, 21_849_999.50m),
            new DailyStatDto(new DateOnly(2026, 11, 2), 0m, 0m, 0m),
            new DailyStatDto(new DateOnly(2026, 11, 3), 0m, 849_999.50m, -849_999.50m)
        ], daily);

        // 3. Monthly: 12 rows for 2026
        var monthly = await clientA.GetApiJsonAsync<MonthlyStatDto[]>("/api/statistics/monthly?year=2026", ct);
        Assert.NotNull(monthly);
        Assert.Equal(12, monthly.Length);
        Assert.Equal(new MonthlyStatDto(2026, 10, 20_000_000.00m, 3_000_000.00m, 17_000_000.00m), monthly[9]);
        Assert.Equal(new MonthlyStatDto(2026, 11, 25_000_000.00m, 2_500_000.00m, 22_500_000.00m), monthly[10]);
        Assert.All(monthly.Where(m => m.Month is not (10 or 11)), m => Assert.Equal((0m, 0m, 0m), (m.Income, m.Expense, m.Balance)));

        // 4. Yearly
        var yearly = await clientA.GetApiJsonAsync<YearlyStatDto[]>("/api/statistics/yearly?fromYear=2026&toYear=2026", ct);
        Assert.NotNull(yearly);
        Assert.Equal([new YearlyStatDto(2026, 45_000_000.00m, 5_500_000.00m, 39_500_000.00m)], yearly);

        // 5. Categories Expense (tie-break Food before Shopping) & Income
        var expenseCats = await clientA.GetApiJsonAsync<CategoryStatDto[]>(
            "/api/statistics/categories?type=Expense&from=2026-11-01&to=2026-11-30",
            ct);
        Assert.NotNull(expenseCats);
        Assert.Equal(
        [
            new CategoryStatDto(7, "Bills", TransactionType.Expense, 1_500_000.00m, 1, 60.00m),
            new CategoryStatDto(1, "Food", TransactionType.Expense, 500_000.00m, 2, 20.00m),
            new CategoryStatDto(3, "Shopping", TransactionType.Expense, 500_000.00m, 1, 20.00m)
        ], expenseCats);

        var incomeCats = await clientA.GetApiJsonAsync<CategoryStatDto[]>(
            "/api/statistics/categories?type=Income&from=2026-11-01&to=2026-11-30",
            ct);
        Assert.NotNull(incomeCats);
        Assert.Equal(
        [
            new CategoryStatDto(8, "Salary", TransactionType.Income, 22_000_000.00m, 1, 88.00m),
            new CategoryStatDto(10, "Freelance", TransactionType.Income, 3_000_000.00m, 1, 12.00m)
        ], incomeCats);

        // Half-open category range & empty category period
        var fromOnlyCats = await clientA.GetApiJsonAsync<CategoryStatDto[]>(
            "/api/statistics/categories?type=Expense&from=2026-11-30",
            ct);
        Assert.NotNull(fromOnlyCats);
        Assert.Equal([new CategoryStatDto(7, "Bills", TransactionType.Expense, 1_500_000.00m, 1, 100.00m)], fromOnlyCats);

        var emptyCats = await clientA.GetApiJsonAsync<CategoryStatDto[]>(
            "/api/statistics/categories?type=Expense&from=2026-09-01&to=2026-09-30",
            ct);
        Assert.NotNull(emptyCats);
        Assert.Empty(emptyCats);

        // 6. Top categories (limit=1 retains 60.00% of total Expense period)
        var topExpense = await clientA.GetApiJsonAsync<CategoryStatDto[]>(
            "/api/statistics/top-categories?type=Expense&limit=1&from=2026-11-01&to=2026-11-30",
            ct);
        Assert.NotNull(topExpense);
        Assert.Equal([new CategoryStatDto(7, "Bills", TransactionType.Expense, 1_500_000.00m, 1, 60.00m)], topExpense);

        // 7. Comparison Nov vs Oct, Jan vs Dec (null percents), and explicit compareTo parameters
        var cmpNov = await clientA.GetApiJsonAsync<MonthComparisonDto>("/api/statistics/comparison?year=2026&month=11", ct);
        Assert.Equal(
            new MonthComparisonDto(
                new PeriodTotalsDto(2026, 11, 25_000_000.00m, 2_500_000.00m, 22_500_000.00m),
                new PeriodTotalsDto(2026, 10, 20_000_000.00m, 3_000_000.00m, 17_000_000.00m),
                5_000_000.00m,
                -500_000.00m,
                5_500_000.00m,
                25.00m,
                -16.67m),
            cmpNov);

        var cmpJan = await clientA.GetApiJsonAsync<MonthComparisonDto>("/api/statistics/comparison?year=2026&month=1", ct);
        Assert.Equal(new PeriodTotalsDto(2025, 12, 0m, 0m, 0m), cmpJan!.Previous);
        Assert.Null(cmpJan.IncomeChangePercent);
        Assert.Null(cmpJan.ExpenseChangePercent);

        var cmpCustom = await clientA.GetApiJsonAsync<MonthComparisonDto>(
            "/api/statistics/comparison?year=2026&month=10&compareToYear=2026&compareToMonth=11",
            ct);
        Assert.Equal(-5_000_000.00m, cmpCustom!.IncomeChange);
    }

    [Fact]
    public async Task InvalidQueriesAndDateRanges_ReturnExact400Codes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, timeProvider: new FakeTimeProvider(FixedNow));
        using var anonClient = factory.CreateClient();

        var user = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(user.Auth.AccessToken);

        string[] validationFailedUrls =
        [
            "/api/statistics/summary?month=11",
            "/api/statistics/daily?from=2026-11-01",
            "/api/statistics/monthly",
            "/api/statistics/categories",
            "/api/statistics/categories?type=1",
            "/api/statistics/comparison?year=2026&month=11&compareToYear=2026"
        ];

        foreach (var url in validationFailedUrls)
        {
            using var resp = await client.GetAsync(url, ct);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Equal("validation_failed", (await resp.ReadProblemDetailsAsync(ct)).Code);
        }

        string[] invalidRangeUrls =
        [
            "/api/statistics/daily?from=2026-11-10&to=2026-11-01",
            "/api/statistics/daily?from=2025-01-01&to=2026-01-02",
            "/api/statistics/yearly?fromYear=2027&toYear=2026",
            "/api/statistics/yearly?fromYear=2000&toYear=2020",
            "/api/statistics/categories?type=Expense&from=2026-11-10&to=2026-11-01",
            "/api/statistics/top-categories?type=Expense&from=2026-11-10&to=2026-11-01"
        ];

        foreach (var url in invalidRangeUrls)
        {
            using var resp = await client.GetAsync(url, ct);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Equal("invalid_date_range", (await resp.ReadProblemDetailsAsync(ct)).Code);
        }
    }

    internal static async Task SeedDatasetDs1Async(HttpClient clientA, HttpClient clientB, CancellationToken ct)
    {
        var userARows = new[]
        {
            new { title = "Salary Oct", amount = 20_000_000.00m, type = "Income", categoryId = 8, transactionDate = "2026-10-05" },
            new { title = "Food Oct", amount = 1_000_000.00m, type = "Expense", categoryId = 1, transactionDate = "2026-10-10" },
            new { title = "Bills Oct", amount = 2_000_000.00m, type = "Expense", categoryId = 7, transactionDate = "2026-10-20" },
            new { title = "Salary Nov", amount = 22_000_000.00m, type = "Income", categoryId = 8, transactionDate = "2026-11-01" },
            new { title = "Food Nov 1", amount = 150_000.50m, type = "Expense", categoryId = 1, transactionDate = "2026-11-01" },
            new { title = "Food Nov 3", amount = 349_999.50m, type = "Expense", categoryId = 1, transactionDate = "2026-11-03" },
            new { title = "Shopping Nov", amount = 500_000.00m, type = "Expense", categoryId = 3, transactionDate = "2026-11-03" },
            new { title = "Freelance Nov", amount = 3_000_000.00m, type = "Income", categoryId = 10, transactionDate = "2026-11-15" },
            new { title = "Bills Nov", amount = 1_500_000.00m, type = "Expense", categoryId = 7, transactionDate = "2026-11-30" }
        };

        foreach (var row in userARows)
        {
            using var resp = await clientA.PostApiJsonAsync("/api/transactions", row, ct);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        }

        var userBRows = new[]
        {
            new { title = "Noise Income B", amount = 99_999_999.00m, type = "Income", categoryId = 8, transactionDate = "2026-11-02" },
            new { title = "Noise Expense B", amount = 88_888_888.00m, type = "Expense", categoryId = 1, transactionDate = "2026-11-02" }
        };

        foreach (var row in userBRows)
        {
            using var resp = await clientB.PostApiJsonAsync("/api/transactions", row, ct);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        }
    }
}
