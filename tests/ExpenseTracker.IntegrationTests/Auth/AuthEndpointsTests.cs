using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Application.Dtos.Auth;
using ExpenseTracker.Application.Dtos.Users;
using ExpenseTracker.Infrastructure.Data;
using ExpenseTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ExpenseTracker.IntegrationTests.Auth;

[Collection(SqlServerCollection.Name)]
public sealed class AuthEndpointsTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Register_ValidAndDuplicateAndWeakPassword_MatchesContract()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var email = AuthHelper.CreateUniqueEmail("reg");

        using var created = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = $"  {email.ToUpperInvariant()}  ", password = AuthHelper.DefaultTestPassword, fullName = "  Minh  " },
            ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/auth/me", created.Headers.Location?.ToString());

        var rawCreated = await created.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("accessToken", rawCreated, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", rawCreated, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", rawCreated, StringComparison.OrdinalIgnoreCase);

        var userDto = await created.Content.ReadFromJsonAsync<UserDto>(ct);
        Assert.NotNull(userDto);
        Assert.Equal(email, userDto.Email);
        Assert.Equal("Minh", userDto.FullName);

        using var duplicate = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = email.ToUpperInvariant(), password = AuthHelper.DefaultTestPassword, fullName = "Other" },
            ct);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var dupProblem = await duplicate.ReadProblemDetailsAsync(ct);
        Assert.Equal("email_already_registered", dupProblem.Code);

        using var weakPassword = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = AuthHelper.CreateUniqueEmail("weak"), password = "NoDigitsHere", fullName = "Weak" },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, weakPassword.StatusCode);
        var weakProblem = await weakPassword.ReadProblemDetailsAsync(ct);
        Assert.Equal("validation_failed", weakProblem.Code);
    }

    [Fact]
    public async Task Login_Refresh_Me_Profile_ChangePassword_AndLogout_EndToEndFlow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(fixture.ConnectionString);
        using var anonClient = factory.CreateClient();

        var session = await AuthHelper.RegisterAndLoginAsync(anonClient, fullName: "Initial Name", cancellationToken: ct);
        Assert.False(string.IsNullOrWhiteSpace(session.Auth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(session.Auth.RefreshToken));

        using var authClient = factory.CreateAuthenticatedClient(session.Auth.AccessToken);

        // GET /api/auth/me
        using var meResponse = await authClient.GetAsync("/api/auth/me", ct);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var meDto = await meResponse.Content.ReadFromJsonAsync<UserDto>(ct);
        Assert.Equal(session.Id, meDto?.Id);

        // PUT /api/auth/profile ignores extra forbidden fields (SEC-11)
        using var profileResponse = await authClient.PutAsJsonAsync(
            "/api/auth/profile",
            new { fullName = "Updated Full Name", email = "attacker@evil.local", userId = 99999 },
            ct);
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        var updatedProfile = await profileResponse.Content.ReadFromJsonAsync<UserDto>(ct);
        Assert.Equal("Updated Full Name", updatedProfile?.FullName);
        Assert.Equal(session.Email, updatedProfile?.Email);

        // POST /api/auth/refresh-token
        using var refreshResponse = await anonClient.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new { refreshToken = session.Auth.RefreshToken },
            ct);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.Equal("no-store", refreshResponse.Headers.CacheControl?.ToString());
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<AuthResponse>(ct);
        Assert.NotNull(refreshed);
        Assert.NotEqual(session.Auth.RefreshToken, refreshed.RefreshToken);

        // PUT /api/auth/change-password wrong current => 400 current_password_incorrect
        using var wrongPassResponse = await authClient.PutAsJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = "WrongPassword1!", newPassword = "NewPassword2!" },
            ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassResponse.StatusCode);
        Assert.Equal("current_password_incorrect", (await wrongPassResponse.ReadProblemDetailsAsync(ct)).Code);

        // POST /api/auth/logout with active rotated token => 204
        using var logoutResponse = await authClient.PostAsJsonAsync(
            "/api/auth/logout",
            new { refreshToken = refreshed.RefreshToken },
            ct);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // PUT /api/auth/change-password valid => 204 and new password works
        using var changePassResponse = await authClient.PutAsJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = session.Password, newPassword = "NewPassword2!" },
            ct);
        Assert.Equal(HttpStatusCode.NoContent, changePassResponse.StatusCode);

        using var newLogin = await anonClient.PostAsJsonAsync(
            "/api/auth/login",
            new { email = session.Email, password = "NewPassword2!" },
            ct);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
        Assert.Equal("no-store", newLogin.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task FakeTimeProvider_SetTo20261215_JwtBearerAcceptsTokenIssuedOnFutureClock()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeClock = new FakeTimeProvider(new DateTimeOffset(2026, 12, 15, 3, 0, 0, TimeSpan.Zero));
        await using var factory = new ApiFactory(fixture.ConnectionString, timeProvider: fakeClock);
        using var anonClient = factory.CreateClient();

        var session = await AuthHelper.RegisterAndLoginAsync(anonClient, cancellationToken: ct);
        using var authClient = factory.CreateAuthenticatedClient(session.Auth.AccessToken);

        using var me = await authClient.GetAsync("/api/auth/me", ct);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task LoginAndRegister_EmailQueries_DoNotWrapEmailColumnInLower()
    {
        var ct = TestContext.Current.CancellationToken;
        var sqlLog = new List<string>();
        var interceptor = new UsersSqlCaptureInterceptor(sqlLog);

        await using var factory = new ApiFactory(
            fixture.ConnectionString,
            configureExtraServices: services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlServer(fixture.ConnectionString).AddInterceptors(interceptor));
            });

        using var client = factory.CreateClient();
        var session = await AuthHelper.RegisterAndLoginAsync(client, cancellationToken: ct);
        Assert.True(session.Id > 0);

        var userSelectQueries = sqlLog
            .Where(sql => sql.Contains("FROM [Users]", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(userSelectQueries);
        Assert.All(userSelectQueries, sql =>
            Assert.DoesNotContain("LOWER(", sql, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class UsersSqlCaptureInterceptor(List<string> sqlLog) : DbCommandInterceptor
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
