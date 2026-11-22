using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Application.Dtos.Categories;
using ExpenseTracker.Application.Dtos.Common;
using ExpenseTracker.Application.Dtos.Transactions;
using ExpenseTracker.Application.Enums;
using ExpenseTracker.Infrastructure.Data;
using ExpenseTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ExpenseTracker.IntegrationTests.Transactions;

[Collection(SqlServerCollection.Name)]
public sealed class TransactionEndpointsTests(SqlServerFixture fixture)
{
    private static readonly DateTimeOffset FixedNow = new(2026, 12, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CrudLifecycle_FullReplaceAndTimestamps_FollowsSpecification()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeClock = new FakeTimeProvider(FixedNow);
        await using var factory = new ApiFactory(fixture.ConnectionString, timeProvider: fakeClock);
        using var anonClient = factory.CreateClient();

        var user = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(user.Auth.AccessToken);

        using var createResponse = await client.PostApiJsonAsync(
            "/api/transactions",
            new
            {
                title = "  Pho Bo  ",
                amount = 55000.50m,
                type = "Expense",
                categoryId = 1,
                description = "  Breakfast near school  ",
                transactionDate = "2026-12-10"
            },
            ct);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);
        var created = await createResponse.Content.ReadApiJsonAsync<TransactionDto>(ct);
        Assert.NotNull(created);
        Assert.Equal("Pho Bo", created.Title);
        Assert.Equal(55000.50m, created.Amount);
        Assert.Equal(TransactionType.Expense, created.Type);
        Assert.Equal(1, created.CategoryId);
        Assert.Equal("Food", created.CategoryName);
        Assert.Equal("Breakfast near school", created.Description);
        Assert.Equal(new DateOnly(2026, 12, 10), created.TransactionDate);
        Assert.EndsWith($"/api/transactions/{created.Id}", createResponse.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);

        var fetched = await client.GetApiJsonAsync<TransactionDto>($"/api/transactions/{created.Id}", ct);
        Assert.Equal(created.Id, fetched?.Id);
        Assert.Equal("Food", fetched?.CategoryName);

        // Advance within the 15-minute access token lifetime so the existing JWT stays valid while UpdatedAt increases.
        fakeClock.Advance(TimeSpan.FromMinutes(5));

        // Full replace PUT without description clears description to null and preserves CreatedAt.
        using var updateResponse = await client.PutApiJsonAsync(
            $"/api/transactions/{created.Id}",
            new
            {
                title = "Monthly Salary",
                amount = 18000000m,
                type = "Income",
                categoryId = 8,
                transactionDate = "2026-12-12"
            },
            ct);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadApiJsonAsync<TransactionDto>(ct);
        Assert.NotNull(updated);
        Assert.Equal("Monthly Salary", updated.Title);
        Assert.Equal(TransactionType.Income, updated.Type);
        Assert.Equal("Salary", updated.CategoryName);
        Assert.Null(updated.Description);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt > created.UpdatedAt);

        using var deleteResponse = await client.DeleteAsync($"/api/transactions/{created.Id}", ct);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var getAfterDelete = await client.GetAsync($"/api/transactions/{created.Id}", ct);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
        Assert.Equal("transaction_not_found", (await getAfterDelete.ReadProblemDetailsAsync(ct)).Code);
    }

    [Fact]
    public async Task ListFilteringSearchSortingAndTieBrokenPaging_WorkWithoutGapsOrDuplicates()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeClock = new FakeTimeProvider(FixedNow);
        var sqlLog = new List<string>();
        var sqlInterceptor = new SqlCaptureInterceptor(sqlLog);

        await using var factory = new ApiFactory(
            fixture.ConnectionString,
            timeProvider: fakeClock,
            configureExtraServices: services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlServer(fixture.ConnectionString).AddInterceptors(sqlInterceptor));
            });

        using var anonClient = factory.CreateClient();
        var user = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        var other = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(user.Auth.AccessToken);
        using var otherClient = factory.CreateAuthenticatedClient(other.Auth.AccessToken);

        // Create a foreign personal category to verify ?categoryId=<foreign> returns empty page (no probing).
        using var otherCatResp = await otherClient.PostApiJsonAsync("/api/categories", new { name = "OtherPrivate", type = "Expense" }, ct);
        var otherCat = await otherCatResp.Content.ReadApiJsonAsync<CategoryDto>(ct);
        Assert.NotNull(otherCat);

        // 5 transactions sharing exact TransactionDate to test stable Id tie-breaker across pages (pageSize=2).
        var createdIds = new List<int>();
        string[] titles = ["Coffee Alpha", "Lunch Beta", "Coffee Delta", "Dinner Gamma", "Coffee Zeta"];
        decimal[] amounts = [30m, 60m, 10m, 90m, 50m];
        int[] cats = [1, 1, 3, 1, 1];

        for (var i = 0; i < titles.Length; i++)
        {
            using var resp = await client.PostApiJsonAsync(
                "/api/transactions",
                new
                {
                    title = titles[i],
                    amount = amounts[i],
                    type = "Expense",
                    categoryId = cats[i],
                    transactionDate = "2026-11-15"
                },
                ct);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var dto = await resp.Content.ReadApiJsonAsync<TransactionDto>(ct);
            createdIds.Add(dto!.Id);
        }

        // One Income row on different date to verify type & date range filters.
        using (var incomeResp = await client.PostApiJsonAsync(
            "/api/transactions",
            new
            {
                title = "Bonus Nov",
                amount = 1000m,
                type = "Income",
                categoryId = 9,
                transactionDate = "2026-11-20"
            },
            ct))
        {
            Assert.Equal(HttpStatusCode.Created, incomeResp.StatusCode);
        }

        sqlLog.Clear();

        var page1 = await client.GetApiJsonAsync<PagedResult<TransactionDto>>("/api/transactions?type=Expense&page=1&pageSize=2&sortBy=date&sortDir=desc", ct);
        var page2 = await client.GetApiJsonAsync<PagedResult<TransactionDto>>("/api/transactions?type=Expense&page=2&pageSize=2&sortBy=date&sortDir=desc", ct);
        var page3 = await client.GetApiJsonAsync<PagedResult<TransactionDto>>("/api/transactions?type=Expense&page=3&pageSize=2&sortBy=date&sortDir=desc", ct);
        var beyond = await client.GetApiJsonAsync<PagedResult<TransactionDto>>("/api/transactions?type=Expense&page=4&pageSize=2&sortBy=date&sortDir=desc", ct);

        Assert.NotNull(page1);
        Assert.NotNull(page2);
        Assert.NotNull(page3);
        Assert.NotNull(beyond);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Empty(beyond.Items);

        var combinedIds = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(t => t.Id).ToArray();
        Assert.Equal(createdIds.OrderDescending().ToArray(), combinedIds);
        Assert.Equal(5, combinedIds.Distinct().Count());

        // Filter by categoryId, from/to, search, sortBy=amount&sortDir=asc (and verify captured SQL is parameterized)
        var filtered = await client.GetApiJsonAsync<PagedResult<TransactionDto>>(
            "/api/transactions?type=Expense&categoryId=1&from=2026-11-15&to=2026-11-15&search=coffee&sortBy=amount&sortDir=asc",
            ct);
        Assert.Equal(["Coffee Alpha", "Coffee Zeta"], filtered!.Items.Select(t => t.Title).ToArray());

        var foreignCatFilter = await client.GetApiJsonAsync<PagedResult<TransactionDto>>(
            $"/api/transactions?categoryId={otherCat.Id}",
            ct);
        Assert.NotNull(foreignCatFilter);
        Assert.Empty(foreignCatFilter.Items);
        Assert.Equal(0, foreignCatFilter.TotalCount);
        Assert.Equal(0, foreignCatFilter.TotalPages);

        // Confirm real SQL Server query contains UserId predicate, SARGable date bounds, parameterized LIKE ESCAPE, and pushed-down OFFSET before JOIN.
        Assert.Contains(sqlLog, sql =>
            sql.Contains("[t].[UserId]", StringComparison.Ordinal) &&
            sql.Contains("[t].[TransactionDate] >=", StringComparison.Ordinal) &&
            sql.Contains("[t].[TransactionDate] <=", StringComparison.Ordinal) &&
            sql.Contains("ESCAPE N'\\'", StringComparison.Ordinal) &&
            !sql.Contains("coffee", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("OFFSET", StringComparison.Ordinal) &&
            sql.Contains("INNER JOIN [Categories]", StringComparison.Ordinal) &&
            sql.IndexOf("OFFSET", StringComparison.Ordinal) < sql.IndexOf("INNER JOIN [Categories]", StringComparison.Ordinal) &&
            sql.Contains("[t].[Id]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidationAndBusinessRules_ReturnExact400And409Contract()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeClock = new FakeTimeProvider(FixedNow);
        await using var factory = new ApiFactory(fixture.ConnectionString, timeProvider: fakeClock);
        using var anonClient = factory.CreateClient();

        var user = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(user.Auth.AccessToken);

        // Missing required fields -> 400 validation_failed with errors dictionary
        using var emptyResp = await client.PostAsJsonAsync("/api/transactions", new { }, ct);
        var emptyProblem = await emptyResp.ReadProblemDetailsAsync(ct);
        Assert.Equal(HttpStatusCode.BadRequest, emptyResp.StatusCode);
        Assert.Equal("validation_failed", emptyProblem.Code);
        Assert.True(emptyProblem.Raw.TryGetProperty("errors", out var errorsElement));
        Assert.True(errorsElement.EnumerateObject().Any());

        // Amount = 0, -5, 10.001, Date < 2000, Date > tomorrow+1, numeric Type JSON body -> 400 validation_failed
        object[] invalidPayloads =
        [
            new { title = "Zero", amount = 0m, type = "Expense", categoryId = 1, transactionDate = "2026-12-15" },
            new { title = "Negative", amount = -5m, type = "Expense", categoryId = 1, transactionDate = "2026-12-15" },
            new { title = "ThreeDp", amount = 10.001m, type = "Expense", categoryId = 1, transactionDate = "2026-12-15" },
            new { title = "TooOld", amount = 10m, type = "Expense", categoryId = 1, transactionDate = "1999-12-31" },
            new { title = "TooFuture", amount = 10m, type = "Expense", categoryId = 1, transactionDate = "2026-12-17" },
            new { title = "IntEnum", amount = 10m, type = 2, categoryId = 1, transactionDate = "2026-12-15" }
        ];

        foreach (var payload in invalidPayloads)
        {
            using var badResp = await client.PostAsJsonAsync("/api/transactions", payload, ct);
            var badProblem = await badResp.ReadProblemDetailsAsync(ct);
            Assert.Equal(HttpStatusCode.BadRequest, badResp.StatusCode);
            Assert.Equal("validation_failed", badProblem.Code);
        }

        // Category type mismatch (Salary is Income; transaction is Expense) -> 400 category_type_mismatch
        using var mismatchResp = await client.PostApiJsonAsync(
            "/api/transactions",
            new
            {
                title = "Mismatch",
                amount = 100m,
                type = "Expense",
                categoryId = 8,
                transactionDate = "2026-12-15"
            },
            ct);
        Assert.Equal(HttpStatusCode.BadRequest, mismatchResp.StatusCode);
        Assert.Equal("category_type_mismatch", (await mismatchResp.ReadProblemDetailsAsync(ct)).Code);

        // Query validation: ?type=1, ?sortBy=hack, ?sortDir=up, ?from>to -> 400
        using var badTypeQuery = await client.GetAsync("/api/transactions?type=1", ct);
        Assert.Equal(HttpStatusCode.BadRequest, badTypeQuery.StatusCode);
        Assert.Equal("validation_failed", (await badTypeQuery.ReadProblemDetailsAsync(ct)).Code);

        using var badSortQuery = await client.GetAsync("/api/transactions?sortBy=userId&sortDir=sideways", ct);
        Assert.Equal(HttpStatusCode.BadRequest, badSortQuery.StatusCode);
        Assert.Equal("validation_failed", (await badSortQuery.ReadProblemDetailsAsync(ct)).Code);

        using var badRangeQuery = await client.GetAsync("/api/transactions?from=2026-12-20&to=2026-12-01", ct);
        Assert.Equal(HttpStatusCode.BadRequest, badRangeQuery.StatusCode);
        Assert.Equal("invalid_date_range", (await badRangeQuery.ReadProblemDetailsAsync(ct)).Code);
    }

    [Fact]
    public async Task CategoryInUseViaRealTransactionApi_BlocksDeleteAndTypeChangeUntilTransactionDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeClock = new FakeTimeProvider(FixedNow);
        await using var factory = new ApiFactory(fixture.ConnectionString, timeProvider: fakeClock);
        using var anonClient = factory.CreateClient();

        var user = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(user.Auth.AccessToken);

        using var catResp = await client.PostApiJsonAsync("/api/categories", new { name = "RealApiInUse", type = "Expense" }, ct);
        var category = await catResp.Content.ReadApiJsonAsync<CategoryDto>(ct);
        Assert.NotNull(category);

        using var txResp = await client.PostApiJsonAsync(
            "/api/transactions",
            new
            {
                title = "Using Personal Category",
                amount = 123.45m,
                type = "Expense",
                categoryId = category.Id,
                transactionDate = "2026-12-10"
            },
            ct);
        Assert.Equal(HttpStatusCode.Created, txResp.StatusCode);
        var tx = await txResp.Content.ReadApiJsonAsync<TransactionDto>(ct);
        Assert.NotNull(tx);

        using var delCatUsed = await client.DeleteAsync($"/api/categories/{category.Id}", ct);
        var delProblem = await delCatUsed.ReadProblemDetailsAsync(ct);
        Assert.Equal(HttpStatusCode.Conflict, delCatUsed.StatusCode);
        Assert.Equal("category_in_use", delProblem.Code);
        Assert.Contains("1 transaction(s)", delProblem.Detail, StringComparison.Ordinal);

        using var putCatType = await client.PutApiJsonAsync(
            $"/api/categories/{category.Id}",
            new { name = "RealApiInUse", type = "Income" },
            ct);
        Assert.Equal(HttpStatusCode.Conflict, putCatType.StatusCode);
        Assert.Equal("category_in_use", (await putCatType.ReadProblemDetailsAsync(ct)).Code);

        using var delTx = await client.DeleteAsync($"/api/transactions/{tx.Id}", ct);
        Assert.Equal(HttpStatusCode.NoContent, delTx.StatusCode);

        using var delCatUnused = await client.DeleteAsync($"/api/categories/{category.Id}", ct);
        Assert.Equal(HttpStatusCode.NoContent, delCatUnused.StatusCode);
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
