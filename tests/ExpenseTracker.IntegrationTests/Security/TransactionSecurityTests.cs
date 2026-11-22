using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Application.Dtos.Categories;
using ExpenseTracker.Application.Dtos.Common;
using ExpenseTracker.Application.Dtos.Transactions;
using ExpenseTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace ExpenseTracker.IntegrationTests.Security;

[Collection(SqlServerCollection.Name)]
public sealed class TransactionSecurityTests(SqlServerFixture fixture)
{
    private static readonly DateTimeOffset FixedNow = new(2026, 12, 15, 3, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("GET", "/api/transactions")]
    [InlineData("GET", "/api/transactions/1")]
    [InlineData("POST", "/api/transactions")]
    [InlineData("PUT", "/api/transactions/1")]
    [InlineData("DELETE", "/api/transactions/1")]
    public async Task SEC01_TransactionEndpoints_WithoutToken_Return401Unauthorized(string method, string path)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, timeProvider: new FakeTimeProvider(FixedNow));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await client.SendAsync(request, ct);
        var problem = await response.ReadProblemDetailsAsync(ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", problem.Code);
    }

    [Fact]
    public async Task SEC06_07_08_09_UserA_CannotReadUpdateDeleteOrList_UserBTransactions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, timeProvider: new FakeTimeProvider(FixedNow));
        using var anonClient = factory.CreateClient();

        var userA = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        var userB = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var clientA = factory.CreateAuthenticatedClient(userA.Auth.AccessToken);
        using var clientB = factory.CreateAuthenticatedClient(userB.Auth.AccessToken);

        using var createB = await clientB.PostApiJsonAsync(
            "/api/transactions",
            new
            {
                title = "Secret UserB Expense",
                amount = 777000m,
                type = "Expense",
                categoryId = 1,
                description = "Do not touch",
                transactionDate = "2026-12-10"
            },
            ct);
        var txB = await createB.Content.ReadApiJsonAsync<TransactionDto>(ct);
        Assert.NotNull(txB);

        // SEC-06: GET foreign id -> 404
        using var getA = await clientA.GetAsync($"/api/transactions/{txB.Id}", ct);
        Assert.Equal(HttpStatusCode.NotFound, getA.StatusCode);
        Assert.Equal("transaction_not_found", (await getA.ReadProblemDetailsAsync(ct)).Code);

        // SEC-07: PUT foreign id -> 404 (even with invalid categoryId, proving 404-before-400 precedence) and DB row untouched
        using var putA = await clientA.PutApiJsonAsync(
            $"/api/transactions/{txB.Id}",
            new
            {
                title = "Hijacked",
                amount = 1m,
                type = "Expense",
                categoryId = 999999,
                transactionDate = "2026-12-10"
            },
            ct);
        Assert.Equal(HttpStatusCode.NotFound, putA.StatusCode);
        Assert.Equal("transaction_not_found", (await putA.ReadProblemDetailsAsync(ct)).Code);

        // SEC-08: DELETE foreign id -> 404 and DB row still exists
        using var deleteA = await clientA.DeleteAsync($"/api/transactions/{txB.Id}", ct);
        Assert.Equal(HttpStatusCode.NotFound, deleteA.StatusCode);
        Assert.Equal("transaction_not_found", (await deleteA.ReadProblemDetailsAsync(ct)).Code);

        // SEC-09: User A owns a row and unfiltered/filtered/searched lists never include UserB's rows
        using var ownA = await clientA.PostApiJsonAsync(
            "/api/transactions",
            new { title = "UserA Own Coffee", amount = 45m, type = "Expense", categoryId = 1, transactionDate = "2026-12-10" },
            ct);
        Assert.Equal(HttpStatusCode.Created, ownA.StatusCode);

        var unfilteredA = await clientA.GetApiJsonAsync<PagedResult<TransactionDto>>("/api/transactions", ct);
        Assert.NotNull(unfilteredA);
        var ownOnly = Assert.Single(unfilteredA.Items);
        Assert.Equal("UserA Own Coffee", ownOnly.Title);

        var listA = await clientA.GetApiJsonAsync<PagedResult<TransactionDto>>("/api/transactions?search=Secret&type=Expense", ct);
        Assert.NotNull(listA);
        Assert.Empty(listA.Items);

        await using var db = fixture.CreateDbContext();
        var persistedB = await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == txB.Id, ct);
        Assert.Equal("Secret UserB Expense", persistedB.Title);
        Assert.Equal(777000m, persistedB.Amount);
        Assert.Equal(userB.Id, persistedB.UserId);
    }

    [Fact]
    public async Task SEC10_SEC11_ForeignCategoryRejected_AndMassAssignmentUserIdIgnored()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, timeProvider: new FakeTimeProvider(FixedNow));
        using var anonClient = factory.CreateClient();

        var userA = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        var userB = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var clientA = factory.CreateAuthenticatedClient(userA.Auth.AccessToken);
        using var clientB = factory.CreateAuthenticatedClient(userB.Auth.AccessToken);

        using var catBResp = await clientB.PostApiJsonAsync("/api/categories", new { name = "B Private", type = "Expense" }, ct);
        var catB = await catBResp.Content.ReadApiJsonAsync<CategoryDto>(ct);
        Assert.NotNull(catB);

        // SEC-10 (POST): foreign personal categoryId -> 400 category_not_found
        using var postForeignCat = await clientA.PostApiJsonAsync(
            "/api/transactions",
            new { title = "Cross Cat", amount = 50m, type = "Expense", categoryId = catB.Id, transactionDate = "2026-12-10" },
            ct);
        Assert.Equal(HttpStatusCode.BadRequest, postForeignCat.StatusCode);
        Assert.Equal("category_not_found", (await postForeignCat.ReadProblemDetailsAsync(ct)).Code);

        // SEC-11 (POST & PUT): extra userId field ignored; row stays owned by User A
        using var createA = await clientA.PostAsJsonAsync(
            "/api/transactions",
            new
            {
                title = "Own MassAssign",
                amount = 50m,
                type = "Expense",
                categoryId = 1,
                transactionDate = "2026-12-10",
                userId = userB.Id
            },
            ct);
        Assert.Equal(HttpStatusCode.Created, createA.StatusCode);
        var txA = await createA.Content.ReadApiJsonAsync<TransactionDto>(ct);
        Assert.NotNull(txA);

        // SEC-10 (PUT): updating own transaction to B's category -> 400 category_not_found
        using var putForeignCat = await clientA.PutApiJsonAsync(
            $"/api/transactions/{txA.Id}",
            new { title = "Cross Cat Put", amount = 60m, type = "Expense", categoryId = catB.Id, transactionDate = "2026-12-10" },
            ct);
        Assert.Equal(HttpStatusCode.BadRequest, putForeignCat.StatusCode);
        Assert.Equal("category_not_found", (await putForeignCat.ReadProblemDetailsAsync(ct)).Code);

        using var putMassAssign = await clientA.PutAsJsonAsync(
            $"/api/transactions/{txA.Id}",
            new
            {
                title = "Own Updated",
                amount = 75m,
                type = "Expense",
                categoryId = 1,
                transactionDate = "2026-12-11",
                userId = userB.Id
            },
            ct);
        Assert.Equal(HttpStatusCode.OK, putMassAssign.StatusCode);

        await using var db = fixture.CreateDbContext();
        var entity = await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == txA.Id, ct);
        Assert.Equal(userA.Id, entity.UserId);
        Assert.Equal("Own Updated", entity.Title);
    }

    [Fact]
    public async Task SEC20_SEC21_SearchEscapesSqlWildcardsAndPayload_AndRejectsOversizedPagingOrSearch()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, timeProvider: new FakeTimeProvider(FixedNow));
        using var anonClient = factory.CreateClient();

        var userA = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        var userB = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var clientA = factory.CreateAuthenticatedClient(userA.Auth.AccessToken);
        using var clientB = factory.CreateAuthenticatedClient(userB.Auth.AccessToken);

        await clientA.PostApiJsonAsync("/api/transactions", new { title = "Normal Coffee", amount = 10m, type = "Expense", categoryId = 1, transactionDate = "2026-12-10" }, ct);
        await clientA.PostApiJsonAsync("/api/transactions", new { title = @"100% Promo [VIP]_Deal\1", amount = 20m, type = "Expense", categoryId = 1, transactionDate = "2026-12-10" }, ct);
        await clientB.PostApiJsonAsync("/api/transactions", new { title = @"100% B Secret _\", amount = 30m, type = "Expense", categoryId = 1, transactionDate = "2026-12-10" }, ct);

        // Literal %, _, [, and \ must match only User A's literal row, never act as wildcards and never expose User B's rows
        var percentResult = await clientA.GetApiJsonAsync<PagedResult<TransactionDto>>("/api/transactions?search=%25", ct);
        Assert.NotNull(percentResult);
        var percentItem = Assert.Single(percentResult.Items);
        Assert.Equal(@"100% Promo [VIP]_Deal\1", percentItem.Title);

        var underscoreResult = await clientA.GetApiJsonAsync<PagedResult<TransactionDto>>("/api/transactions?search=_", ct);
        Assert.Equal(@"100% Promo [VIP]_Deal\1", Assert.Single(underscoreResult!.Items).Title);

        var backslashResult = await clientA.GetApiJsonAsync<PagedResult<TransactionDto>>("/api/transactions?search=%5C", ct);
        Assert.Equal(@"100% Promo [VIP]_Deal\1", Assert.Single(backslashResult!.Items).Title);

        var bracketResult = await clientA.GetApiJsonAsync<PagedResult<TransactionDto>>("/api/transactions?search=%5BVIP%5D_Deal", ct);
        Assert.Single(bracketResult!.Items);

        // SQL injection string executes safely and returns 200 with 0 matches
        var sqliResult = await clientA.GetApiJsonAsync<PagedResult<TransactionDto>>(
            $"/api/transactions?search={Uri.EscapeDataString("' OR 1=1 --")}",
            ct);
        Assert.NotNull(sqliResult);
        Assert.Empty(sqliResult.Items);

        // SEC-21 caps
        string[] invalidCapUrls =
        [
            "/api/transactions?pageSize=100000",
            "/api/transactions?pageSize=0",
            "/api/transactions?page=0",
            $"/api/transactions?search={new string('a', 101)}"
        ];

        foreach (var url in invalidCapUrls)
        {
            using var resp = await clientA.GetAsync(url, ct);
            var problem = await resp.ReadProblemDetailsAsync(ct);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Equal("validation_failed", problem.Code);
        }
    }
}
