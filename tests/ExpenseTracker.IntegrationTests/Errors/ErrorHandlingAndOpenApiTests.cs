using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ExpenseTracker.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseTracker.IntegrationTests.Errors;

[Collection(SqlServerCollection.Name)]
public sealed class ErrorHandlingAndOpenApiTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task AllProblemDetailsResponses_HaveUniformRfc9457ShapeCodeTraceIdAndNoSniffHeader()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, environmentName: "Production");
        using var anonClient = factory.CreateClient();

        var user = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(user.Auth.AccessToken);

        // 400 Malformed JSON
        using var badJsonContent = new StringContent("{ invalid json", Encoding.UTF8, "application/json");
        using var malformedResp = await client.PostAsync("/api/categories", badJsonContent, ct);
        await AssertProblemShapeAsync(malformedResp, HttpStatusCode.BadRequest, "validation_failed", ct);

        // 400 Invalid DateOnly parse in body
        using var badDateContent = new StringContent(
            """{"title":"Bad Date","amount":10,"type":"Expense","categoryId":1,"transactionDate":"2026-99-99"}""",
            Encoding.UTF8,
            "application/json");
        using var badDateResp = await client.PostAsync("/api/transactions", badDateContent, ct);
        await AssertProblemShapeAsync(badDateResp, HttpStatusCode.BadRequest, "validation_failed", ct);

        // 400 Invalid Date Range
        using var badRangeResp = await client.GetAsync("/api/statistics/daily?from=2026-11-10&to=2026-11-01", ct);
        await AssertProblemShapeAsync(badRangeResp, HttpStatusCode.BadRequest, "invalid_date_range", ct);

        // 401 Unauthorized
        using var anonProtectedResp = await anonClient.GetAsync("/api/categories", ct);
        await AssertProblemShapeAsync(anonProtectedResp, HttpStatusCode.Unauthorized, "unauthorized", ct);

        // 403 Forbidden (system category)
        using var forbiddenResp = await client.DeleteAsync("/api/categories/1", ct);
        await AssertProblemShapeAsync(forbiddenResp, HttpStatusCode.Forbidden, "system_category_readonly", ct);

        // 404 NotFound (missing resource + unknown route via StatusCodePages)
        using var missingResourceResp = await client.GetAsync("/api/transactions/99999999", ct);
        await AssertProblemShapeAsync(missingResourceResp, HttpStatusCode.NotFound, "transaction_not_found", ct);

        using var unknownRouteResp = await client.GetAsync("/api/does-not-exist", ct);
        await AssertProblemShapeAsync(unknownRouteResp, HttpStatusCode.NotFound, "not_found", ct);

        // 405 Method Not Allowed (via StatusCodePages)
        using var methodNotAllowedResp = await client.PostAsJsonAsync("/api/statistics/summary", new { }, ct);
        await AssertProblemShapeAsync(methodNotAllowedResp, HttpStatusCode.MethodNotAllowed, "method_not_allowed", ct);

        // 409 Conflict
        using var conflictResp = await client.PostApiJsonAsync("/api/categories", new { name = "Food", type = "Expense" }, ct);
        await AssertProblemShapeAsync(conflictResp, HttpStatusCode.Conflict, "category_name_exists", ct);

        // 415 Unsupported Media Type (via AddProblemDetails CustomizeProblemDetails)
        using var plainText = new StringContent("plain text", Encoding.UTF8, "text/plain");
        using var unsupportedMediaResp = await client.PostAsync("/api/categories", plainText, ct);
        await AssertProblemShapeAsync(unsupportedMediaResp, HttpStatusCode.UnsupportedMediaType, "unsupported_media_type", ct);
    }

    [Fact]
    public async Task SEC22_UnhandledExceptionInProduction_ReturnsGeneric500WithoutStackTrace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(
            fixture.ConnectionString,
            environmentName: "Production",
            configureExtraServices: services =>
                services.AddControllers().AddApplicationPart(typeof(TestFaultController).Assembly));

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/test-diagnostics/unhandled", ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        Assert.Equal("internal_error", root.GetProperty("code").GetString());
        Assert.Equal("An unexpected error occurred.", root.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("Sensitive internal SQL", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", raw, StringComparison.OrdinalIgnoreCase);

        // Regression test: Accept: text/plain must still return ProblemDetails, never empty body or rethrown 500
        using var plainFaultReq = new HttpRequestMessage(HttpMethod.Get, "/test-diagnostics/unhandled");
        plainFaultReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        using var plainFaultResp = await client.SendAsync(plainFaultReq, ct);
        await AssertProblemShapeAsync(plainFaultResp, HttpStatusCode.InternalServerError, "internal_error", ct);
    }

    [Fact]
    public async Task ExceptionResponses_WithNonJsonAcceptHeader_StillEmitProblemDetailsJson()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, environmentName: "Production");
        using var anonClient = factory.CreateClient();

        var user = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client = factory.CreateAuthenticatedClient(user.Auth.AccessToken);

        // 404 NotFoundException with Accept: text/plain must still emit ProblemDetails JSON
        using var notFoundReq = new HttpRequestMessage(HttpMethod.Get, "/api/transactions/99999999");
        notFoundReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        using var notFoundResp = await client.SendAsync(notFoundReq, ct);
        await AssertProblemShapeAsync(notFoundResp, HttpStatusCode.NotFound, "transaction_not_found", ct);

        // 403 ForbiddenException with Accept: text/plain
        using var forbiddenReq = new HttpRequestMessage(HttpMethod.Delete, "/api/categories/1");
        forbiddenReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        using var forbiddenResp = await client.SendAsync(forbiddenReq, ct);
        await AssertProblemShapeAsync(forbiddenResp, HttpStatusCode.Forbidden, "system_category_readonly", ct);
    }

    [Fact]
    public async Task SEC25_OpenApiAndSwagger_Return404ProblemDetailsWhenDisabled_AndServeMetadataWhenEnabled()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var disabledFactory = new ApiFactory(fixture.ConnectionString, openApiEnableUi: false, environmentName: "Production"))
        {
            using var disabledClient = disabledFactory.CreateClient();

            foreach (var path in new[] { "/swagger", "/swagger/index.html", "/openapi/v1.json" })
            {
                using var resp = await disabledClient.GetAsync(path, ct);
                await AssertProblemShapeAsync(resp, HttpStatusCode.NotFound, "not_found", ct);
            }
        }

        await using (var enabledFactory = new ApiFactory(fixture.ConnectionString, openApiEnableUi: true, environmentName: "Development"))
        {
            using var enabledClient = enabledFactory.CreateClient();

            using var openApiResp = await enabledClient.GetAsync("/openapi/v1.json", ct);
            Assert.Equal(HttpStatusCode.OK, openApiResp.StatusCode);

            using var openApiDoc = JsonDocument.Parse(await openApiResp.Content.ReadAsStringAsync(ct));
            Assert.Equal("Personal Expense Tracker API", openApiDoc.RootElement.GetProperty("info").GetProperty("title").GetString());
            Assert.True(openApiDoc.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));

            var paths = openApiDoc.RootElement.GetProperty("paths");
            Assert.False(paths.GetProperty("/api/auth/register").GetProperty("post").TryGetProperty("security", out _));
            Assert.False(paths.GetProperty("/api/auth/login").GetProperty("post").TryGetProperty("security", out _));
            Assert.False(paths.GetProperty("/api/auth/refresh-token").GetProperty("post").TryGetProperty("security", out _));
            Assert.True(paths.GetProperty("/api/categories").GetProperty("get").TryGetProperty("security", out _));
            Assert.True(paths.GetProperty("/api/transactions").GetProperty("get").TryGetProperty("security", out _));
            Assert.True(paths.GetProperty("/api/statistics/summary").GetProperty("get").TryGetProperty("security", out _));

            // OpenAPI schema must serialize enums as string literals and populate XML summaries.
            var transactionTypeSchema = openApiDoc.RootElement
                .GetProperty("components")
                .GetProperty("schemas")
                .GetProperty("TransactionType");
            if (transactionTypeSchema.TryGetProperty("type", out var enumSchemaType))
            {
                Assert.Equal("string", enumSchemaType.GetString());
            }

            var enumElements = transactionTypeSchema.GetProperty("enum").EnumerateArray().ToArray();
            Assert.All(enumElements, e => Assert.Equal(JsonValueKind.String, e.ValueKind));
            Assert.Equal(["Income", "Expense"], enumElements.Select(e => e.GetString()).ToArray());

            var categorySummary = paths.GetProperty("/api/categories").GetProperty("get").GetProperty("summary").GetString();
            Assert.False(string.IsNullOrWhiteSpace(categorySummary));
        }
    }

    [Fact]
    public async Task RateLimit429_EmitsProblemDetailsWithRateLimitedCode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(
            fixture.ConnectionString,
            environmentName: "Production",
            rateLimitPermitLimit: 1);

        using var client = factory.CreateClient();

        using var first = await client.PostAsJsonAsync("/api/auth/login", new { email = "a@b.com", password = "wrong" }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);

        using var second = await client.PostAsJsonAsync("/api/auth/login", new { email = "a@b.com", password = "wrong" }, ct);
        Assert.NotNull(second.Headers.RetryAfter?.Delta);
        Assert.True(second.Headers.RetryAfter.Delta.Value.TotalSeconds > 0);
        await AssertProblemShapeAsync(second, HttpStatusCode.TooManyRequests, "rate_limited", ct);
    }

    private static async Task AssertProblemShapeAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode,
        CancellationToken ct)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        MediaTypeHeaderValue? contentType = response.Content.Headers.ContentType;
        Assert.Equal("application/problem+json", contentType?.MediaType);
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));

        var problem = await response.ReadProblemDetailsAsync(ct);
        Assert.Equal((int)expectedStatus, problem.Status);
        Assert.Equal(expectedCode, problem.Code);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
        Assert.True(problem.Raw.TryGetProperty("type", out var typeProp) && !string.IsNullOrWhiteSpace(typeProp.GetString()));
        Assert.True(problem.Raw.TryGetProperty("title", out var titleProp) && !string.IsNullOrWhiteSpace(titleProp.GetString()));
        Assert.True(problem.Raw.TryGetProperty("detail", out _));
    }
}
