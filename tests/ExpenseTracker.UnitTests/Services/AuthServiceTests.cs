using System.Security.Cryptography;
using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Common;
using ExpenseTracker.Application.Dtos.Auth;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Exceptions;
using ExpenseTracker.Infrastructure.Auth;
using ExpenseTracker.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using AuthServiceImpl = ExpenseTracker.Application.Services.Auth.AuthService;

namespace ExpenseTracker.UnitTests.Services;

public sealed class AuthServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 12, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RegisterAsync_ValidRequest_NormalizesEmailTrimsNameAndHashesPassword()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var service = CreateAuthService(db, new FakeTimeProvider(FixedNow));

        var result = await service.RegisterAsync(new RegisterRequest
        {
            Email = "  Alice.Example@Mail.COM  ",
            Password = "Password1!",
            FullName = "  Alice Nguyen  "
        }, ct);

        Assert.Equal("alice.example@mail.com", result.Email);
        Assert.Equal("Alice Nguyen", result.FullName);

        var savedUser = await db.Users.SingleAsync(ct);
        Assert.NotEqual("Password1!", savedUser.PasswordHash);
        Assert.Equal("alice.example@mail.com", savedUser.Email);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmailDifferentCase_ThrowsConflictException()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var service = CreateAuthService(db, new FakeTimeProvider(FixedNow));

        await service.RegisterAsync(new RegisterRequest
        {
            Email = "alice@example.com",
            Password = "Password1!",
            FullName = "Alice"
        }, ct);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RegisterAsync(new RegisterRequest
            {
                Email = "ALICE@EXAMPLE.COM",
                Password = "Password1!",
                FullName = "Alice Two"
            }, ct));

        Assert.Equal("email_already_registered", ex.Code);
    }

    [Fact]
    public async Task RegisterAsync_WeakPasswordWithoutDigit_ThrowsValidationFailedException()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var service = CreateAuthService(db, new FakeTimeProvider(FixedNow));

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            service.RegisterAsync(new RegisterRequest
            {
                Email = "alice@example.com",
                Password = "OnlyLettersHere",
                FullName = "Alice"
            }, ct));

        Assert.Equal("validation_failed", ex.Code);
        Assert.True(ex.Errors.ContainsKey("password"));
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokensAndStoresHashedRefreshTokenOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var service = CreateAuthService(db, new FakeTimeProvider(FixedNow));

        await service.RegisterAsync(new RegisterRequest
        {
            Email = "bob@example.com",
            Password = "Password1!",
            FullName = "Bob"
        }, ct);

        var response = await service.LoginAsync(new LoginRequest
        {
            Email = "  BOB@example.com ",
            Password = "Password1!"
        }, ct);

        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(response.RefreshToken));

        var storedToken = await db.RefreshTokens.SingleAsync(ct);
        Assert.NotEqual(response.RefreshToken, storedToken.TokenHash);
        Assert.Null(storedToken.RevokedAt);
    }

    [Fact]
    public async Task LoginAsync_UnknownEmail_RunsDummyPasswordVerificationAndThrowsInvalidCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var mockHasher = new Mock<IPasswordHasher<User>>();
        mockHasher
            .Setup(h => h.VerifyHashedPassword(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PasswordVerificationResult.Failed);

        var service = CreateAuthService(db, new FakeTimeProvider(FixedNow), passwordHasher: mockHasher.Object);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.LoginAsync(new LoginRequest
            {
                Email = "missing@example.com",
                Password = "Password1!"
            }, ct));

        Assert.Equal("invalid_credentials", ex.Code);
        mockHasher.Verify(
            h => h.VerifyHashedPassword(It.IsAny<User>(), It.IsAny<string>(), "Password1!"),
            Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsInvalidCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var service = CreateAuthService(db, new FakeTimeProvider(FixedNow));

        await service.RegisterAsync(new RegisterRequest
        {
            Email = "bob@example.com",
            Password = "Password1!",
            FullName = "Bob"
        }, ct);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.LoginAsync(new LoginRequest
            {
                Email = "bob@example.com",
                Password = "WrongPassword1!"
            }, ct));

        Assert.Equal("invalid_credentials", ex.Code);
    }

    [Fact]
    public async Task LoginAsync_SuccessRehashNeeded_UpdatesStoredPasswordHash()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        db.Users.Add(new User
        {
            Email = "legacy@example.com",
            PasswordHash = "OLD_HASH",
            FullName = "Legacy",
            CreatedAt = FixedNow.UtcDateTime,
            UpdatedAt = FixedNow.UtcDateTime
        });
        await db.SaveChangesAsync(ct);

        var mockHasher = new Mock<IPasswordHasher<User>>();
        mockHasher
            .Setup(h => h.VerifyHashedPassword(It.IsAny<User>(), "OLD_HASH", "Password1!"))
            .Returns(PasswordVerificationResult.SuccessRehashNeeded);
        mockHasher
            .Setup(h => h.HashPassword(It.IsAny<User>(), "Password1!"))
            .Returns("UPGRADED_HASH");

        var service = CreateAuthService(db, new FakeTimeProvider(FixedNow), passwordHasher: mockHasher.Object);
        await service.LoginAsync(new LoginRequest { Email = "legacy@example.com", Password = "Password1!" }, ct);

        var user = await db.Users.SingleAsync(ct);
        Assert.Equal("UPGRADED_HASH", user.PasswordHash);
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_RevokesOldTokenAndIssuesNewPair()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var clock = new FakeTimeProvider(FixedNow);
        var service = CreateAuthService(db, clock);

        await service.RegisterAsync(new RegisterRequest
        {
            Email = "alice@example.com",
            Password = "Password1!",
            FullName = "Alice"
        }, ct);
        var initial = await service.LoginAsync(new LoginRequest
        {
            Email = "alice@example.com",
            Password = "Password1!"
        }, ct);

        clock.Advance(TimeSpan.FromMinutes(10));
        var rotated = await service.RefreshAsync(new RefreshTokenRequest { RefreshToken = initial.RefreshToken }, ct);

        Assert.NotEqual(initial.RefreshToken, rotated.RefreshToken);
        var tokens = await db.RefreshTokens.OrderBy(t => t.Id).ToListAsync(ct);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(FixedNow.UtcDateTime.AddMinutes(10), tokens[0].RevokedAt);
        Assert.Equal(tokens[1].TokenHash, tokens[0].ReplacedByTokenHash);
        Assert.Null(tokens[1].RevokedAt);
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ThrowsRefreshTokenInvalid()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var clock = new FakeTimeProvider(FixedNow);
        var service = CreateAuthService(db, clock);

        await service.RegisterAsync(new RegisterRequest
        {
            Email = "alice@example.com",
            Password = "Password1!",
            FullName = "Alice"
        }, ct);
        var login = await service.LoginAsync(new LoginRequest
        {
            Email = "alice@example.com",
            Password = "Password1!"
        }, ct);

        clock.Advance(TimeSpan.FromDays(8));
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken }, ct));

        Assert.Equal("refresh_token_invalid", ex.Code);
    }

    [Fact]
    public async Task RefreshAsync_RevokedTokenReuseEvenAfterExpiry_RevokesAllActiveUserTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var clock = new FakeTimeProvider(FixedNow);
        var service = CreateAuthService(db, clock);

        await service.RegisterAsync(new RegisterRequest
        {
            Email = "alice@example.com",
            Password = "Password1!",
            FullName = "Alice"
        }, ct);

        var session1 = await service.LoginAsync(new LoginRequest { Email = "alice@example.com", Password = "Password1!" }, ct);
        var session2 = await service.LoginAsync(new LoginRequest { Email = "alice@example.com", Password = "Password1!" }, ct);

        var rotated1 = await service.RefreshAsync(new RefreshTokenRequest { RefreshToken = session1.RefreshToken }, ct);
        Assert.NotNull(rotated1.RefreshToken);

        // Advance past session1's original expiry: revocation check must still run first and revoke sibling sessions.
        clock.Advance(TimeSpan.FromDays(8));

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.RefreshAsync(new RefreshTokenRequest { RefreshToken = session1.RefreshToken }, ct));

        Assert.Equal("refresh_token_invalid", ex.Code);
        Assert.All(await db.RefreshTokens.ToListAsync(ct), t => Assert.NotNull(t.RevokedAt));
        Assert.NotNull(session2.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_UnknownToken_ThrowsRefreshTokenInvalid()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var service = CreateAuthService(db, new FakeTimeProvider(FixedNow));

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.RefreshAsync(new RefreshTokenRequest { RefreshToken = "nonexistent-token" }, ct));

        Assert.Equal("refresh_token_invalid", ex.Code);
    }

    [Fact]
    public async Task LogoutAsync_OwnAndForeignTokens_RevokesOnlyCallersToken()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var clock = new FakeTimeProvider(FixedNow);
        var currentUserMock = new Mock<ICurrentUserService>();

        var bootstrapService = CreateAuthService(db, clock, currentUserMock.Object);
        var userA = await bootstrapService.RegisterAsync(new RegisterRequest { Email = "a@example.com", Password = "Password1!", FullName = "User A" }, ct);
        var userB = await bootstrapService.RegisterAsync(new RegisterRequest { Email = "b@example.com", Password = "Password1!", FullName = "User B" }, ct);

        var loginA = await bootstrapService.LoginAsync(new LoginRequest { Email = "a@example.com", Password = "Password1!" }, ct);
        var loginB = await bootstrapService.LoginAsync(new LoginRequest { Email = "b@example.com", Password = "Password1!" }, ct);

        currentUserMock.SetupGet(c => c.UserId).Returns(userA.Id);

        // Foreign token: no-op, must not revoke User B's token.
        await bootstrapService.LogoutAsync(new RefreshTokenRequest { RefreshToken = loginB.RefreshToken }, ct);

        // Unknown token: idempotent no-op.
        await bootstrapService.LogoutAsync(new RefreshTokenRequest { RefreshToken = "unknown-token" }, ct);

        // Own token: revoked.
        await bootstrapService.LogoutAsync(new RefreshTokenRequest { RefreshToken = loginA.RefreshToken }, ct);

        var tokens = await db.RefreshTokens.AsNoTracking().ToListAsync(ct);
        var tokenA = Assert.Single(tokens, t => t.UserId == userA.Id);
        var tokenB = Assert.Single(tokens, t => t.UserId == userB.Id);
        Assert.NotNull(tokenA.RevokedAt);
        Assert.Null(tokenB.RevokedAt);
    }

    [Fact]
    public async Task LogoutAsync_WhenConcurrentSaveThrowsConcurrencyException_StaysIdempotentWithoutThrowing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var clock = new FakeTimeProvider(FixedNow);
        var currentUserMock = new Mock<ICurrentUserService>();

        var bootstrapService = CreateAuthService(db, clock, currentUserMock.Object);
        var user = await bootstrapService.RegisterAsync(
            new RegisterRequest { Email = "race@example.com", Password = "Password1!", FullName = "Race User" },
            ct);
        var login = await bootstrapService.LoginAsync(
            new LoginRequest { Email = "race@example.com", Password = "Password1!" },
            ct);

        currentUserMock.SetupGet(c => c.UserId).Returns(user.Id);

        var concurrencyDb = new ConcurrencyThrowingDbContext(db);
        var service = CreateAuthService(concurrencyDb, clock, currentUserMock.Object);

        await service.LogoutAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken }, ct);

        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AuthUnit_{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private static AuthServiceImpl CreateAuthService(
        IAppDbContext db,
        TimeProvider timeProvider,
        ICurrentUserService? currentUserService = null,
        IPasswordHasher<User>? passwordHasher = null)
    {
        var keyBytes = new byte[48];
        RandomNumberGenerator.Fill(keyBytes);

        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "ExpenseTracker.Unit",
            Audience = "ExpenseTracker.UnitClient",
            Key = Convert.ToBase64String(keyBytes),
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        });

        var tokenService = new JwtTokenService(jwtOptions, timeProvider);
        var appClock = new AppClock(timeProvider, AppClock.ResolveTimeZone("Asia/Ho_Chi_Minh"));

        return new AuthServiceImpl(
            db,
            tokenService,
            passwordHasher ?? new PasswordHasher<User>(),
            currentUserService ?? Mock.Of<ICurrentUserService>(),
            appClock,
            NullLogger<AuthServiceImpl>.Instance);
    }

    private sealed class ConcurrencyThrowingDbContext(AppDbContext inner) : IAppDbContext
    {
        public DbSet<User> Users => inner.Users;

        public DbSet<RefreshToken> RefreshTokens => inner.RefreshTokens;

        public DbSet<Category> Categories => inner.Categories;

        public DbSet<Transaction> Transactions => inner.Transactions;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new DbUpdateConcurrencyException("Simulated concurrent token update.");

        public Task<bool> CanConnectAsync(CancellationToken cancellationToken) =>
            inner.CanConnectAsync(cancellationToken);

        public void ClearChangeTracker() =>
            inner.ClearChangeTracker();

        public Task<int> RevokeAllActiveTokensForUserAsync(
            int userId,
            DateTime revokedAtUtc,
            CancellationToken cancellationToken) =>
            inner.RevokeAllActiveTokensForUserAsync(userId, revokedAtUtc, cancellationToken);
    }
}
