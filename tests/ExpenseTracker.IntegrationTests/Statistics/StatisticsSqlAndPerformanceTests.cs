using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using ExpenseTracker.Application.Dtos.Common;
using ExpenseTracker.Application.Dtos.Statistics;
using ExpenseTracker.Application.Dtos.Transactions;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Enums;
using ExpenseTracker.Infrastructure.Data;
using ExpenseTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ExpenseTracker.IntegrationTests.Statistics;

[Collection(SqlServerCollection.Name)]
public sealed class StatisticsSqlAndPerformanceTests(SqlServerFixture fixture, ITestOutputHelper output)
{
    private static readonly DateTimeOffset FixedNow = new(2026, 12, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AllSevenEndpoints_TranslateAggregationsToSql_AndRespondUnder300MsFor10000Rows()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeClock = new FakeTimeProvider(FixedNow);
        var sqlLog = new List<string>();
        var interceptor = new SqlCaptureInterceptor(sqlLog);

        await using var factory = new ApiFactory(
            fixture.ConnectionString,
            timeProvider: fakeClock,
            configureExtraServices: services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlServer(fixture.ConnectionString).AddInterceptors(interceptor));
            });

        using var anonClient = factory.CreateClient();
        var user = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(user.Auth.AccessToken);

        // Seed 10,000 rows for this user across 2024..2026 and categories 1..12 directly into SQL Server.
        await using (var db = fixture.CreateDbContext())
        {
            var rng = new Random(20261122);
            var rows = new List<Transaction>(10_000);
            var startDate = new DateOnly(2024, 1, 1);

            for (var i = 0; i < 10_000; i++)
            {
                var isIncome = i % 5 == 0;
                var categoryId = isIncome ? rng.Next(8, 13) : rng.Next(1, 8);
                rows.Add(new Transaction
                {
                    UserId = user.Id,
                    CategoryId = categoryId,
                    Type = isIncome ? TransactionType.Income : TransactionType.Expense,
                    Title = $"Perf Row {i + 1}",
                    Amount = Math.Round((decimal)(rng.NextDouble() * 500_000d + 1_000d), 2),
                    Description = null,
                    TransactionDate = startDate.AddDays(i % 1080),
                    CreatedAt = FixedNow.UtcDateTime,
                    UpdatedAt = FixedNow.UtcDateTime
                });
            }

            db.Transactions.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }

        // Warm up EF query compilation and JIT once across endpoints before timing.
        var endpoints = new (string Name, string Url)[]
        {
            ("summary", "/api/statistics/summary?year=2026&month=11"),
            ("summary-alltime", "/api/statistics/summary"),
            ("daily", "/api/statistics/daily?from=2026-10-01&to=2026-11-30"),
            ("monthly", "/api/statistics/monthly?year=2026"),
            ("yearly", "/api/statistics/yearly?fromYear=2024&toYear=2026"),
            ("categories", "/api/statistics/categories?type=Expense&from=2026-01-01&to=2026-12-31"),
            ("top-categories", "/api/statistics/top-categories?type=Expense&limit=5&from=2026-01-01&to=2026-12-31"),
            ("comparison", "/api/statistics/comparison?year=2026&month=11"),
            ("transactions-default", "/api/transactions?page=1&pageSize=20"),
            ("transactions-categoryId", "/api/transactions?categoryId=1&page=1&pageSize=20")
        };

        foreach (var (_, url) in endpoints)
        {
            using var warmup = await client.GetAsync(url, ct);
            Assert.Equal(HttpStatusCode.OK, warmup.StatusCode);
        }

        foreach (var (name, url) in endpoints)
        {
            sqlLog.Clear();
            var sw = Stopwatch.StartNew();
            using var resp = await client.GetAsync(url, ct);
            sw.Stop();

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            Assert.True(elapsedMs < 300d, $"Endpoint '{name}' exceeded 300 ms target: {elapsedMs:F2} ms");

            if (name.StartsWith("transactions-", StringComparison.Ordinal))
            {
                var paged = await resp.Content.ReadApiJsonAsync<PagedResult<TransactionDto>>(ct);
                Assert.NotNull(paged);
            }
            else if (name.StartsWith("summary", StringComparison.Ordinal))
            {
                Assert.NotNull(await resp.Content.ReadApiJsonAsync<SummaryDto>(ct));
            }
            else if (name == "comparison")
            {
                Assert.NotNull(await resp.Content.ReadApiJsonAsync<MonthComparisonDto>(ct));
            }

            if (name is "summary" or "summary-alltime" or "daily" or "monthly" or "yearly" or "categories" or "top-categories" or "comparison")
            {
                var expectedSqlRoundTrips = name == "comparison" ? 2 : 1;
                Assert.Equal(expectedSqlRoundTrips, sqlLog.Count);
                Assert.All(sqlLog, sql =>
                {
                    Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("SUM(", sql, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("[t].[UserId] = @userId", sql, StringComparison.Ordinal);

                    var whereIndex = sql.IndexOf("WHERE ", StringComparison.OrdinalIgnoreCase);
                    Assert.True(whereIndex >= 0);
                    Assert.DoesNotContain("DATEPART", sql[whereIndex..], StringComparison.OrdinalIgnoreCase);
                });

                if (name is "monthly" or "yearly")
                {
                    Assert.Contains(sqlLog, sql =>
                        sql.Contains("[t].[TransactionDate] >=", StringComparison.Ordinal) &&
                        sql.Contains("[t].[TransactionDate] <=", StringComparison.Ordinal));
                }
            }

            var line = string.Format(
                CultureInfo.InvariantCulture,
                "[PERF-10K] {0,-24} {1,7:F2} ms | SQL={2}",
                name,
                elapsedMs,
                sqlLog.LastOrDefault()?.Replace(Environment.NewLine, " ", StringComparison.Ordinal) ?? "(none)");
            output.WriteLine(line);
            TestContext.Current.SendDiagnosticMessage(line);
        }
    }

    private sealed class SqlCaptureInterceptor(List<string> sqlLog) : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            sqlLog.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            sqlLog.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
