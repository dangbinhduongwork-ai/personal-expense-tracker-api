using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Application.Dtos.Categories;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Enums;
using ExpenseTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.IntegrationTests.Categories;

[Collection(SqlServerCollection.Name)]
public sealed class CategoryEndpointsTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task GetAll_Returns12SeededSystemCategoriesAndSupportsTypeFilter()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();

        var session = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(session.Auth.AccessToken);

        using var personalCreate = await client.PostAsJsonAsync(
            "/api/categories",
            new { name = "Cafe", type = "Expense" },
            ct);
        Assert.Equal(HttpStatusCode.Created, personalCreate.StatusCode);

        var all = await client.GetApiJsonAsync<CategoryDto[]>("/api/categories", ct);
        Assert.NotNull(all);
        Assert.Equal(13, all.Length);
        Assert.Equal(12, all.Count(c => c.IsSystem));
        Assert.Equal(1, all.Count(c => !c.IsSystem));
        Assert.True(all[^1].Name == "Cafe" && !all[^1].IsSystem);

        var incomeOnly = await client.GetApiJsonAsync<CategoryDto[]>("/api/categories?type=Income", ct);
        Assert.NotNull(incomeOnly);
        Assert.Equal(5, incomeOnly.Length);
        Assert.All(incomeOnly, c => Assert.Equal(TransactionType.Income, c.Type));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("Income,Expense")]
    [InlineData("InvalidType")]
    public async Task GetAll_NumericOrUnknownEnumQueryString_Returns400ValidationFailed(string rawType)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();
        var session = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(session.Auth.AccessToken);

        using var response = await client.GetAsync($"/api/categories?type={rawType}", ct);
        var problem = await response.ReadProblemDetailsAsync(ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", problem.Code);
    }

    [Fact]
    public async Task GetAll_DuplicateQueryKey_Returns400ValidationFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();
        var session = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(session.Auth.AccessToken);

        using var response = await client.GetAsync("/api/categories?type=Income&type=Expense", ct);
        var problem = await response.ReadProblemDetailsAsync(ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", problem.Code);
    }

    [Fact]
    public async Task CreateAndPut_NameBoundariesIntegerBodyAndUpdateDuplicate_MatchContract()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();
        var session = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(session.Auth.AccessToken);

        // Numeric enum in JSON body -> 400 validation_failed
        using var numericBody = await client.PostAsJsonAsync(
            "/api/categories",
            new { name = "NumericEnumBody", type = 1 },
            ct);
        Assert.Equal(HttpStatusCode.BadRequest, numericBody.StatusCode);
        Assert.Equal("validation_failed", (await numericBody.ReadProblemDetailsAsync(ct)).Code);

        // Name length 101 -> 400 validation_failed; length 100 -> 201 Created
        using var tooLong = await client.PostAsJsonAsync(
            "/api/categories",
            new { name = new string('A', 101), type = "Expense" },
            ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("validation_failed", (await tooLong.ReadProblemDetailsAsync(ct)).Code);

        using var exactMax = await client.PostAsJsonAsync(
            "/api/categories",
            new { name = new string('B', 100), type = "Expense" },
            ct);
        Assert.Equal(HttpStatusCode.Created, exactMax.StatusCode);

        using var second = await client.PostAsJsonAsync(
            "/api/categories",
            new { name = "SecondCat", type = "Expense" },
            ct);
        var secondDto = await second.Content.ReadApiJsonAsync<CategoryDto>(ct);
        Assert.NotNull(secondDto);

        // PUT duplicate name against system or another personal category -> 409 category_name_exists
        using var putDup = await client.PutAsJsonAsync(
            $"/api/categories/{secondDto.Id}",
            new { name = "Food", type = "Expense" },
            ct);
        Assert.Equal(HttpStatusCode.Conflict, putDup.StatusCode);
        Assert.Equal("category_name_exists", (await putDup.ReadProblemDetailsAsync(ct)).Code);
    }

    [Fact]
    public async Task CrudHappyPath_AndValidationAndDuplicateConflicts_MatchContract()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();
        var session = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(session.Auth.AccessToken);

        // Whitespace-only name rejected -> 400
        using var badCreate = await client.PostAsJsonAsync(
            "/api/categories",
            new { name = "   ", type = "Expense" },
            ct);
        Assert.Equal(HttpStatusCode.BadRequest, badCreate.StatusCode);
        Assert.Equal("validation_failed", (await badCreate.ReadProblemDetailsAsync(ct)).Code);

        // Valid create -> 201 + Location
        using var createResponse = await client.PostAsJsonAsync(
            "/api/categories",
            new { name = "  Subscriptions  ", type = "Expense" },
            ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadApiJsonAsync<CategoryDto>(ct);
        Assert.NotNull(created);
        Assert.Equal($"/api/categories/{created.Id}", createResponse.Headers.Location?.AbsolutePath);
        Assert.Equal("Subscriptions", created.Name);
        Assert.False(created.IsSystem);

        // Duplicate own (case-insensitive) -> 409
        using var dupOwn = await client.PostAsJsonAsync(
            "/api/categories",
            new { name = "SUBSCRIPTIONS", type = "Expense" },
            ct);
        Assert.Equal(HttpStatusCode.Conflict, dupOwn.StatusCode);
        Assert.Equal("category_name_exists", (await dupOwn.ReadProblemDetailsAsync(ct)).Code);

        // Duplicate system category name (same type) -> 409
        using var dupSystem = await client.PostAsJsonAsync(
            "/api/categories",
            new { name = "food", type = "Expense" },
            ct);
        Assert.Equal(HttpStatusCode.Conflict, dupSystem.StatusCode);
        Assert.Equal("category_name_exists", (await dupSystem.ReadProblemDetailsAsync(ct)).Code);

        // Same name as system category in opposite type -> 201
        using var oppositeType = await client.PostAsJsonAsync(
            "/api/categories",
            new { name = "Food", type = "Income" },
            ct);
        Assert.Equal(HttpStatusCode.Created, oppositeType.StatusCode);

        // Update happy path -> 200
        using var updateResponse = await client.PutAsJsonAsync(
            $"/api/categories/{created.Id}",
            new { name = "Monthly Apps", type = "Expense" },
            ct);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadApiJsonAsync<CategoryDto>(ct);
        Assert.Equal("Monthly Apps", updated?.Name);

        // GetById -> 200
        using var getResponse = await client.GetAsync($"/api/categories/{created.Id}", ct);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // Delete unused -> 204, then GetById -> 404
        using var deleteResponse = await client.DeleteAsync($"/api/categories/{created.Id}", ct);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var afterDelete = await client.GetAsync($"/api/categories/{created.Id}", ct);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
        Assert.Equal("category_not_found", (await afterDelete.ReadProblemDetailsAsync(ct)).Code);
    }

    [Fact]
    public async Task UpdateTypeAndDelete_WhenCategoryUsedByDirectDbTransaction_Return409CategoryInUse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();
        var session = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(session.Auth.AccessToken);

        using var createResponse = await client.PostAsJsonAsync(
            "/api/categories",
            new { name = "Freelance Tools", type = "Expense" },
            ct);
        var created = await createResponse.Content.ReadApiJsonAsync<CategoryDto>(ct);
        Assert.NotNull(created);

        // Seed a Transaction directly via AppDbContext to exercise FK usage guard.
        await using (var db = fixture.CreateDbContext())
        {
            db.Transactions.Add(new Transaction
            {
                UserId = session.Id,
                CategoryId = created.Id,
                Title = "JetBrains",
                Amount = 150000m,
                Type = TransactionType.Expense,
                TransactionDate = new DateOnly(2026, 11, 22),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }

        // Keeping same Type on rename succeeds even when in use
        using var sameTypeUpdate = await client.PutAsJsonAsync(
            $"/api/categories/{created.Id}",
            new { name = "Software Tools", type = "Expense" },
            ct);
        Assert.Equal(HttpStatusCode.OK, sameTypeUpdate.StatusCode);

        // Changing Type while in use returns 409 category_in_use
        using var changeTypeConflict = await client.PutAsJsonAsync(
            $"/api/categories/{created.Id}",
            new { name = "Software Tools", type = "Income" },
            ct);
        var updateProblem = await changeTypeConflict.ReadProblemDetailsAsync(ct);
        Assert.Equal(HttpStatusCode.Conflict, changeTypeConflict.StatusCode);
        Assert.Equal("category_in_use", updateProblem.Code);
        Assert.Contains("1 transaction(s)", updateProblem.Detail, StringComparison.Ordinal);

        // Deleting while in use returns 409 category_in_use
        using var deleteConflict = await client.DeleteAsync($"/api/categories/{created.Id}", ct);
        var deleteProblem = await deleteConflict.ReadProblemDetailsAsync(ct);
        Assert.Equal(HttpStatusCode.Conflict, deleteConflict.StatusCode);
        Assert.Equal("category_in_use", deleteProblem.Code);
        Assert.Contains("1 transaction(s)", deleteProblem.Detail, StringComparison.Ordinal);
    }
}
