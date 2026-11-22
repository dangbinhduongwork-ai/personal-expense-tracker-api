using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Application.Dtos.Auth;
using ExpenseTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ExpenseTracker.IntegrationTests.Security;

[Collection(SqlServerCollection.Name)]
public sealed class AuthSecurityTests(SqlServerFixture fixture)
{
    [Theory]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("PUT", "/api/auth/profile")]
    [InlineData("PUT", "/api/auth/change-password")]
    [InlineData("POST", "/api/auth/logout")]
    public async Task SEC01_ProtectedEndpoints_WithoutToken_Return401ProblemDetails(string method, string path)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await client.SendAsync(request, ct);
        var problem = await response.ReadProblemDetailsAsync(ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(401, problem.Status);
        Assert.Equal("unauthorized", problem.Code);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [Fact]
    public async Task SEC02_ExpiredAccessTokenBeyond30SecondClockSkew_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 12, 15, 3, 0, 0, TimeSpan.Zero));
        await using var factory = new ApiFactory(fixture.ConnectionString, timeProvider: clock);
        using var anonClient = factory.CreateClient();

        var session = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var authClient = factory.CreateAuthenticatedClient(session.Auth.AccessToken);

        clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(45));

        using var response = await authClient.GetAsync("/api/auth/me", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", (await response.ReadProblemDetailsAsync(ct)).Code);
    }

    [Fact]
    public async Task SEC03_TokenWithTamperedSignature_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();

        var session = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        var parts = session.Auth.AccessToken.Split('.');
        var tamperedSignature = parts[2][..^2] + (parts[2].EndsWith("AA", StringComparison.Ordinal) ? "BB" : "AA");
        var tamperedJwt = $"{parts[0]}.{parts[1]}.{tamperedSignature}";

        using var authClient = factory.CreateAuthenticatedClient(tamperedJwt);
        using var response = await authClient.GetAsync("/api/auth/me", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", (await response.ReadProblemDetailsAsync(ct)).Code);
    }

    [Theory]
    [InlineData("Wrong.Issuer", ApiFactory.DefaultJwtAudience)]
    [InlineData(ApiFactory.DefaultJwtIssuer, "Wrong.Audience")]
    public async Task SEC04_TokenWithWrongIssuerOrAudience_Returns401(string issuer, string audience)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();
        var session = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);

        var token = ForgeSignedJwt(
            factory.JwtSigningKey,
            SecurityAlgorithms.HmacSha256,
            issuer,
            audience,
            userId: session.Id);

        using var client = factory.CreateAuthenticatedClient(token);
        using var response = await client.GetAsync("/api/auth/me", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", (await response.ReadProblemDetailsAsync(ct)).Code);
    }

    [Fact]
    public async Task SEC05_AlgNoneAndHS512SignedWithValidKey_Return401()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();
        var session = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);

        // 1. Unsigned alg=none token for an existing user id
        var headerJson = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
        var payloadJson = Base64UrlEncoder.Encode(
            $$"""{"sub":"{{session.Id}}","email":"{{session.Email}}","iss":"{{ApiFactory.DefaultJwtIssuer}}","aud":"{{ApiFactory.DefaultJwtAudience}}","exp":{{DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()}}}""");
        var algNoneJwt = $"{headerJson}.{payloadJson}.";

        using var algNoneClient = factory.CreateAuthenticatedClient(algNoneJwt);
        using var algNoneResponse = await algNoneClient.GetAsync("/api/auth/me", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, algNoneResponse.StatusCode);
        Assert.Equal("unauthorized", (await algNoneResponse.ReadProblemDetailsAsync(ct)).Code);

        // 2. Different symmetric algorithm (HS512) signed with the valid signing key for an existing user id
        var hs512Jwt = ForgeSignedJwt(
            factory.JwtSigningKey,
            SecurityAlgorithms.HmacSha512,
            ApiFactory.DefaultJwtIssuer,
            ApiFactory.DefaultJwtAudience,
            userId: session.Id);

        using var hs512Client = factory.CreateAuthenticatedClient(hs512Jwt);
        using var hs512Response = await hs512Client.GetAsync("/api/auth/me", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, hs512Response.StatusCode);
        Assert.Equal("unauthorized", (await hs512Response.ReadProblemDetailsAsync(ct)).Code);
    }

    [Fact]
    public async Task SEC15_DatabaseAndResponses_NeverExposePlaintextSecretsOrHashes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var session = await AuthHelper.RegisterAndLoginAsync(client, cancellationToken: ct);
        using var authClient = factory.CreateAuthenticatedClient(session.Auth.AccessToken);

        using var meResponse = await authClient.GetAsync("/api/auth/me", ct);
        var meJson = await meResponse.Content.ReadAsStringAsync(ct);

        using var refreshResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new { refreshToken = session.Auth.RefreshToken },
            ct);
        var refreshJson = await refreshResponse.Content.ReadAsStringAsync(ct);

        Assert.DoesNotContain("passwordHash", meJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", meJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", refreshJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", refreshJson, StringComparison.OrdinalIgnoreCase);

        await using var db = fixture.CreateDbContext();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == session.Id, ct);
        var refreshToken = await db.RefreshTokens.AsNoTracking()
            .Where(rt => rt.UserId == session.Id)
            .OrderBy(rt => rt.Id)
            .FirstAsync(ct);

        Assert.NotEqual(session.Password, user.PasswordHash);
        Assert.NotEqual(session.Auth.RefreshToken, refreshToken.TokenHash);
    }

    [Fact]
    public async Task SEC16_ReusingRotatedRefreshToken_Returns401AndRevokesSiblingActiveSessions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var session1 = await AuthHelper.RegisterAndLoginAsync(client, cancellationToken: ct);

        using var login2Response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = session1.Email, password = session1.Password },
            ct);
        var session2 = await login2Response.Content.ReadFromJsonAsync<AuthResponse>(ct);
        Assert.NotNull(session2);

        // Rotate session1 cleanly
        using var rotateResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new { refreshToken = session1.Auth.RefreshToken },
            ct);
        Assert.Equal(HttpStatusCode.OK, rotateResponse.StatusCode);

        // Replay old revoked token from session1 => 401 and all sibling tokens revoked
        using var replayResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new { refreshToken = session1.Auth.RefreshToken },
            ct);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
        Assert.Equal("refresh_token_invalid", (await replayResponse.ReadProblemDetailsAsync(ct)).Code);

        // Sibling session2 token must now be revoked too
        using var siblingResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new { refreshToken = session2.RefreshToken },
            ct);
        Assert.Equal(HttpStatusCode.Unauthorized, siblingResponse.StatusCode);
    }

    [Fact]
    public async Task SEC17_Logout_RevokesOwnTokenButForeignTokenReturns204WithoutRevokingOwner()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();

        var userA = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        var userB = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);

        using var clientA = factory.CreateAuthenticatedClient(userA.Auth.AccessToken);

        // User A presents User B's refresh token => 204 no-op, User B token remains usable
        using var foreignLogout = await clientA.PostAsJsonAsync(
            "/api/auth/logout",
            new { refreshToken = userB.Auth.RefreshToken },
            ct);
        Assert.Equal(HttpStatusCode.NoContent, foreignLogout.StatusCode);

        using var userBRefresh = await anonClient.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new { refreshToken = userB.Auth.RefreshToken },
            ct);
        Assert.Equal(HttpStatusCode.OK, userBRefresh.StatusCode);

        // User A revokes own token => subsequent refresh returns 401
        using var ownLogout = await clientA.PostAsJsonAsync(
            "/api/auth/logout",
            new { refreshToken = userA.Auth.RefreshToken },
            ct);
        Assert.Equal(HttpStatusCode.NoContent, ownLogout.StatusCode);

        using var userARefreshAfterLogout = await anonClient.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new { refreshToken = userA.Auth.RefreshToken },
            ct);
        Assert.Equal(HttpStatusCode.Unauthorized, userARefreshAfterLogout.StatusCode);
    }

    [Fact]
    public async Task SEC18_ChangePassword_RevokesAllRefreshTokensAndRejectsOldPassword()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();

        var user = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var authClient = factory.CreateAuthenticatedClient(user.Auth.AccessToken);

        using var changeResponse = await authClient.PutAsJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = user.Password, newPassword = "UpdatedPass2!" },
            ct);
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        using var oldPasswordLogin = await anonClient.PostAsJsonAsync(
            "/api/auth/login",
            new { email = user.Email, password = user.Password },
            ct);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        using var refreshAfterChange = await anonClient.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new { refreshToken = user.Auth.RefreshToken },
            ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterChange.StatusCode);
    }

    [Fact]
    public async Task SEC19_LoginWithUnknownEmailVsWrongPassword_ReturnsIdenticalStatusAndProblemBodyShape()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();

        var existingUser = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);

        using var wrongPassword = await anonClient.PostAsJsonAsync(
            "/api/auth/login",
            new { email = existingUser.Email, password = "WrongPassword1!" },
            ct);

        using var unknownEmail = await anonClient.PostAsJsonAsync(
            "/api/auth/login",
            new { email = AuthHelper.CreateUniqueEmail("missing"), password = "WrongPassword1!" },
            ct);

        var wrongProblem = await wrongPassword.ReadProblemDetailsAsync(ct);
        var unknownProblem = await unknownEmail.ReadProblemDetailsAsync(ct);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(wrongProblem.Code, unknownProblem.Code);
        Assert.Equal("invalid_credentials", wrongProblem.Code);
        Assert.Equal(wrongProblem.Detail, unknownProblem.Detail);
    }

    [Fact]
    public async Task SEC23_AuthRateLimit_ExceedingTenRequestsPerMinute_Returns429RateLimited()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, rateLimitPermitLimit: 10);
        using var client = factory.CreateClient();

        var loginPayload = new { email = AuthHelper.CreateUniqueEmail("ratelimit"), password = "WrongPassword1!" };

        for (var i = 0; i < 10; i++)
        {
            using var attempt = await client.PostAsJsonAsync("/api/auth/login", loginPayload, ct);
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        using var throttled = await client.PostAsJsonAsync("/api/auth/login", loginPayload, ct);
        var problem = await throttled.ReadProblemDetailsAsync(ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.NotNull(throttled.Headers.RetryAfter?.Delta);
        Assert.True(throttled.Headers.RetryAfter.Delta.Value.TotalSeconds > 0);
        Assert.Equal(429, problem.Status);
        Assert.Equal("rate_limited", problem.Code);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [Fact]
    public async Task SEC23_ChangePasswordRateLimit_ExceedingTenRequestsPerMinute_Returns429RateLimited()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString, rateLimitPermitLimit: 10);
        using var anonClient = factory.CreateClient();

        var session = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var authClient = factory.CreateAuthenticatedClient(session.Auth.AccessToken);

        var wrongPayload = new { currentPassword = "WrongPassword1!", newPassword = "NewPassword2!" };

        // Register (1) + Login (2) already consumed 2 of 10 permits from the same localhost partition.
        for (var i = 0; i < 8; i++)
        {
            using var attempt = await authClient.PutAsJsonAsync("/api/auth/change-password", wrongPayload, ct);
            Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
        }

        using var throttled = await authClient.PutAsJsonAsync("/api/auth/change-password", wrongPayload, ct);
        var problem = await throttled.ReadProblemDetailsAsync(ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.NotNull(throttled.Headers.RetryAfter?.Delta);
        Assert.True(throttled.Headers.RetryAfter.Delta.Value.TotalSeconds > 0);
        Assert.Equal(429, problem.Status);
        Assert.Equal("rate_limited", problem.Code);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [Fact]
    public async Task Logout_ConcurrentDuplicateRequests_BothReturn204Idempotently()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();

        var session = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var client1 = factory.CreateAuthenticatedClient(session.Auth.AccessToken);
        using var client2 = factory.CreateAuthenticatedClient(session.Auth.AccessToken);

        var task1 = client1.PostAsJsonAsync("/api/auth/logout", new { refreshToken = session.Auth.RefreshToken }, ct);
        var task2 = client2.PostAsJsonAsync("/api/auth/logout", new { refreshToken = session.Auth.RefreshToken }, ct);

        var responses = await Task.WhenAll(task1, task2);
        try
        {
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        }
        finally
        {
            foreach (var r in responses)
            {
                r.Dispose();
            }
        }

        using var refreshAfterLogout = await anonClient.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new { refreshToken = session.Auth.RefreshToken },
            ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ConcurrentWithRefreshToken_Returns204AndRevokesAllTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();

        var session1 = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var login2Resp = await anonClient.PostAsJsonAsync(
            "/api/auth/login",
            new { email = session1.Email, password = session1.Password },
            ct);
        var session2 = await login2Resp.Content.ReadFromJsonAsync<AuthResponse>(ct);
        Assert.NotNull(session2);

        using var authClient = factory.CreateAuthenticatedClient(session1.Auth.AccessToken);
        using var refreshClient = factory.CreateClient();

        var changeTask = authClient.PutAsJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = session1.Password, newPassword = "UpdatedPass2!" },
            ct);
        var refreshTask = refreshClient.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new { refreshToken = session1.Auth.RefreshToken },
            ct);

        await Task.WhenAll(changeTask, refreshTask);
        using var changeResp = await changeTask;
        using var refreshResp = await refreshTask;

        Assert.Equal(HttpStatusCode.NoContent, changeResp.StatusCode);
        Assert.True(
            refreshResp.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized,
            $"Unexpected refresh status during concurrent password change: {refreshResp.StatusCode}");

        if (refreshResp.StatusCode == HttpStatusCode.OK)
        {
            var rotated = await refreshResp.Content.ReadFromJsonAsync<AuthResponse>(ct);
            Assert.NotNull(rotated);
            using var postChangeRotatedRefresh = await anonClient.PostAsJsonAsync(
                "/api/auth/refresh-token",
                new { refreshToken = rotated.RefreshToken },
                ct);
            Assert.Equal(HttpStatusCode.Unauthorized, postChangeRotatedRefresh.StatusCode);
        }

        using var postChangeSession2Refresh = await anonClient.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new { refreshToken = session2.RefreshToken },
            ct);
        Assert.Equal(HttpStatusCode.Unauthorized, postChangeSession2Refresh.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 32 Base64 chars = 24 decoded bytes (< 32 bytes required)
    public async Task SEC24_HostStartup_WithMissingOrShortJwtKey_FailsFastWithOptionsValidation(string invalidKey)
    {
        await using var factory = new ApiFactory(
            fixture.ConnectionString,
            migrateOnStartup: false,
            jwtKeyOverride: invalidKey);

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    private static string ForgeSignedJwt(
        string base64Key,
        string algorithm,
        string issuer,
        string audience,
        int userId)
    {
        var keyBytes = Convert.FromBase64String(base64Key);
        var handler = new JsonWebTokenHandler();
        var utcNow = DateTime.UtcNow;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                [JwtRegisteredClaimNames.Email] = "forged@test.local",
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
            },
            NotBefore = utcNow,
            IssuedAt = utcNow,
            Expires = utcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), algorithm)
        };

        return handler.CreateToken(descriptor);
    }
}
