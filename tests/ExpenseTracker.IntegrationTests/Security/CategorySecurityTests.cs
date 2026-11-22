using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Application.Dtos.Categories;
using ExpenseTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.IntegrationTests.Security;

[Collection(SqlServerCollection.Name)]
public sealed class CategorySecurityTests(SqlServerFixture fixture)
{
    [Theory]
    [InlineData("GET", "/api/categories")]
    [InlineData("GET", "/api/categories/1")]
    [InlineData("POST", "/api/categories")]
    [InlineData("PUT", "/api/categories/1")]
    [InlineData("DELETE", "/api/categories/1")]
    public async Task SEC01_CategoryEndpoints_WithoutToken_Return401Unauthorized(string method, string path)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await client.SendAsync(request, ct);
        var problem = await response.ReadProblemDetailsAsync(ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", problem.Code);
    }

    [Fact]
    public async Task SEC09_CategoryList_NeverContainsOtherUsersPersonalCategories()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();

        var userA = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        var userB = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);

        using var clientA = factory.CreateAuthenticatedClient(userA.Auth.AccessToken);
        using var clientB = factory.CreateAuthenticatedClient(userB.Auth.AccessToken);

        using var createB = await clientB.PostAsJsonAsync(
            "/api/categories",
            new { name = "UserB Private Tag", type = "Expense" },
            ct);
        Assert.Equal(HttpStatusCode.Created, createB.StatusCode);

        var listA = await clientA.GetApiJsonAsync<CategoryDto[]>("/api/categories", ct);
        Assert.NotNull(listA);
        Assert.DoesNotContain(listA, c => c.Name == "UserB Private Tag");

        var expenseListA = await clientA.GetApiJsonAsync<CategoryDto[]>("/api/categories?type=Expense", ct);
        Assert.NotNull(expenseListA);
        Assert.DoesNotContain(expenseListA, c => c.Name == "UserB Private Tag");

        // User A creating same name + type as User B's personal category succeeds (no cross-user conflict oracle)
        using var sameNameCreateA = await clientA.PostAsJsonAsync(
            "/api/categories",
            new { name = "UserB Private Tag", type = "Expense" },
            ct);
        Assert.Equal(HttpStatusCode.Created, sameNameCreateA.StatusCode);
    }

    [Fact]
    public async Task SEC11_CategoryPostAndPut_IgnoreExtraUserIdAndIsSystemFields()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();

        var userA = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        var userB = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);

        using var clientA = factory.CreateAuthenticatedClient(userA.Auth.AccessToken);

        using var createResponse = await clientA.PostAsJsonAsync(
            "/api/categories",
            new { name = "MassAssignTest", type = "Expense", userId = userB.Id, isSystem = true },
            ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadApiJsonAsync<CategoryDto>(ct);
        Assert.NotNull(created);
        Assert.False(created.IsSystem);

        using var updateResponse = await clientA.PutAsJsonAsync(
            $"/api/categories/{created.Id}",
            new { name = "MassAssignUpdated", type = "Expense", userId = userB.Id, isSystem = true },
            ct);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadApiJsonAsync<CategoryDto>(ct);
        Assert.NotNull(updated);
        Assert.False(updated.IsSystem);

        await using var db = fixture.CreateDbContext();
        var entity = await db.Categories.AsNoTracking().SingleAsync(c => c.Id == created.Id, ct);
        Assert.Equal(userA.Id, entity.UserId);
    }

    [Fact]
    public async Task SEC12_UserA_GetPutDelete_UserBPersonalCategory_Returns404WithoutModifyingOwner()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();

        var userA = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        var userB = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);

        using var clientA = factory.CreateAuthenticatedClient(userA.Auth.AccessToken);
        using var clientB = factory.CreateAuthenticatedClient(userB.Auth.AccessToken);

        using var createB = await clientB.PostAsJsonAsync(
            "/api/categories",
            new { name = "B Exclusive", type = "Expense" },
            ct);
        var categoryB = await createB.Content.ReadApiJsonAsync<CategoryDto>(ct);
        Assert.NotNull(categoryB);

        using var getA = await clientA.GetAsync($"/api/categories/{categoryB.Id}", ct);
        Assert.Equal(HttpStatusCode.NotFound, getA.StatusCode);
        Assert.Equal("category_not_found", (await getA.ReadProblemDetailsAsync(ct)).Code);

        using var putA = await clientA.PutAsJsonAsync(
            $"/api/categories/{categoryB.Id}",
            new { name = "Hijacked", type = "Expense" },
            ct);
        Assert.Equal(HttpStatusCode.NotFound, putA.StatusCode);
        Assert.Equal("category_not_found", (await putA.ReadProblemDetailsAsync(ct)).Code);

        using var deleteA = await clientA.DeleteAsync($"/api/categories/{categoryB.Id}", ct);
        Assert.Equal(HttpStatusCode.NotFound, deleteA.StatusCode);
        Assert.Equal("category_not_found", (await deleteA.ReadProblemDetailsAsync(ct)).Code);

        // User B still sees untouched original category
        var ownerCopy = await clientB.GetApiJsonAsync<CategoryDto>($"/api/categories/{categoryB.Id}", ct);
        Assert.Equal("B Exclusive", ownerCopy?.Name);
    }

    [Fact]
    public async Task SEC13_PutAndDeleteSystemCategory_Returns403SystemCategoryReadonly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();

        var user = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(user.Auth.AccessToken);

        using var putSystem = await client.PutAsJsonAsync(
            "/api/categories/1",
            new { name = "Tampered Food", type = "Expense" },
            ct);
        Assert.Equal(HttpStatusCode.Forbidden, putSystem.StatusCode);
        Assert.Equal("system_category_readonly", (await putSystem.ReadProblemDetailsAsync(ct)).Code);

        using var deleteSystem = await client.DeleteAsync("/api/categories/1", ct);
        Assert.Equal(HttpStatusCode.Forbidden, deleteSystem.StatusCode);
        Assert.Equal("system_category_readonly", (await deleteSystem.ReadProblemDetailsAsync(ct)).Code);
    }
}
