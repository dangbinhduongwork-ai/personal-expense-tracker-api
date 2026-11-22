using System.Net;
using ExpenseTracker.IntegrationTests.Infrastructure;

namespace ExpenseTracker.IntegrationTests.Cors;

[Collection(SqlServerCollection.Name)]
public sealed class CorsTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Preflight_AllowedOriginInDevelopment_ReturnsOriginWithoutCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, environmentName: "Development");
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:3000", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Preflight_BlockedOriginOrProductionEmptyOrigins_OmitsAllowOriginHeader()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var devFactory = new ApiFactory(fixture.ConnectionString, environmentName: "Development"))
        {
            using var devClient = devFactory.CreateClient();
            using var blockedReq = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
            blockedReq.Headers.Add("Origin", "https://evil.example");
            blockedReq.Headers.Add("Access-Control-Request-Method", "POST");

            using var blockedResp = await devClient.SendAsync(blockedReq, ct);
            Assert.False(blockedResp.Headers.Contains("Access-Control-Allow-Origin"));
        }

        await using (var prodFactory = new ApiFactory(fixture.ConnectionString, environmentName: "Production"))
        {
            using var prodClient = prodFactory.CreateClient();
            using var prodReq = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
            prodReq.Headers.Add("Origin", "http://localhost:3000");
            prodReq.Headers.Add("Access-Control-Request-Method", "POST");

            using var prodResp = await prodClient.SendAsync(prodReq, ct);
            Assert.False(prodResp.Headers.Contains("Access-Control-Allow-Origin"));
        }
    }

    [Fact]
    public async Task ActualGetRequest_AllowedAndBlockedOrigins_EmitExpectedCorsHeaders()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, environmentName: "Development");
        using var client = factory.CreateClient();

        using var allowedGet = new HttpRequestMessage(HttpMethod.Get, "/health");
        allowedGet.Headers.Add("Origin", "http://localhost:3000");
        using var allowedResp = await client.SendAsync(allowedGet, ct);
        Assert.Equal(HttpStatusCode.OK, allowedResp.StatusCode);
        Assert.Equal("http://localhost:3000", allowedResp.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(allowedResp.Headers.Contains("Access-Control-Allow-Credentials"));

        using var blockedGet = new HttpRequestMessage(HttpMethod.Get, "/health");
        blockedGet.Headers.Add("Origin", "https://evil.example");
        using var blockedResp = await client.SendAsync(blockedGet, ct);
        Assert.Equal(HttpStatusCode.OK, blockedResp.StatusCode);
        Assert.False(blockedResp.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("null")]
    [InlineData("https://*.example.com")]
    [InlineData("ftp://invalid.example.com")]
    public void Startup_UnsafeCorsOriginInProduction_ThrowsInvalidOperationException(string origin)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var factory = new ApiFactory(fixture.ConnectionString, environmentName: "Production")
                .WithWebHostBuilder(builder =>
                {
                    builder.UseSetting("Cors:AllowedOrigins:0", origin);
                });
            _ = factory.CreateClient();
        });

        Assert.Contains("forbidden outside Development", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
