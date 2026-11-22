using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Common;
using ExpenseTracker.Application.Dtos.Users;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Exceptions;
using ExpenseTracker.Application.Services.Users;
using ExpenseTracker.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace ExpenseTracker.UnitTests.Services;

public sealed class UserServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 12, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetMeAsync_ExistingUser_ReturnsUserDto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var user = SeedUser(db);
        await db.SaveChangesAsync(ct);

        var service = CreateUserService(db, new FakeTimeProvider(FixedNow), user.Id);
        var dto = await service.GetMeAsync(ct);

        Assert.Equal(user.Id, dto.Id);
        Assert.Equal(user.Email, dto.Email);
        Assert.Equal(user.FullName, dto.FullName);
    }

    [Fact]
    public async Task GetMeAsync_MissingUser_ThrowsUnauthorizedException()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var service = CreateUserService(db, new FakeTimeProvider(FixedNow), currentUserId: 999);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.GetMeAsync(ct));
    }

    [Fact]
    public async Task UpdateProfileAsync_ValidRequest_TrimsNameAndUpdatesTimestamp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var clock = new FakeTimeProvider(FixedNow);
        var user = SeedUser(db);
        await db.SaveChangesAsync(ct);

        clock.Advance(TimeSpan.FromHours(1));
        var service = CreateUserService(db, clock, user.Id);
        var dto = await service.UpdateProfileAsync(new UpdateProfileRequest { FullName = "  Updated Name  " }, ct);

        Assert.Equal("Updated Name", dto.FullName);
        var saved = await db.Users.SingleAsync(ct);
        Assert.Equal(FixedNow.UtcDateTime.AddHours(1), saved.UpdatedAt);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPassword_ThrowsCurrentPasswordIncorrectAndKeepsTokensActive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var user = SeedUser(db, activeTokens: 1);
        await db.SaveChangesAsync(ct);

        var service = CreateUserService(db, new FakeTimeProvider(FixedNow), user.Id);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ChangePasswordAsync(new ChangePasswordRequest
            {
                CurrentPassword = "WrongPassword1!",
                NewPassword = "NewPassword2!"
            }, ct));

        Assert.Equal("current_password_incorrect", ex.Code);
        Assert.Null((await db.RefreshTokens.SingleAsync(ct)).RevokedAt);
    }

    [Fact]
    public async Task ChangePasswordAsync_SameAsCurrentPassword_ThrowsValidationWithoutRevokingTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var user = SeedUser(db, activeTokens: 1);
        await db.SaveChangesAsync(ct);

        var service = CreateUserService(db, new FakeTimeProvider(FixedNow), user.Id);
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            service.ChangePasswordAsync(new ChangePasswordRequest
            {
                CurrentPassword = "Password1!",
                NewPassword = "Password1!"
            }, ct));

        Assert.Equal("validation_failed", ex.Code);
        Assert.True(ex.Errors.ContainsKey("newPassword"));
        Assert.Null((await db.RefreshTokens.SingleAsync(ct)).RevokedAt);
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidRequest_RehashesPasswordAndRevokesAllActiveTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var clock = new FakeTimeProvider(FixedNow);
        var user = SeedUser(db, activeTokens: 2);
        await db.SaveChangesAsync(ct);

        clock.Advance(TimeSpan.FromMinutes(5));
        var service = CreateUserService(db, clock, user.Id);

        await service.ChangePasswordAsync(new ChangePasswordRequest
        {
            CurrentPassword = "Password1!",
            NewPassword = "NewPassword2!"
        }, ct);

        var hasher = new PasswordHasher<User>();
        var updated = await db.Users.SingleAsync(ct);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(updated, updated.PasswordHash, "NewPassword2!"));
        Assert.All(await db.RefreshTokens.ToListAsync(ct), token => Assert.Equal(clock.GetUtcNow().UtcDateTime, token.RevokedAt));
    }

    private static User SeedUser(AppDbContext db, int activeTokens = 0)
    {
        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Email = "user@example.com",
            FullName = "Test User",
            CreatedAt = FixedNow.UtcDateTime,
            UpdatedAt = FixedNow.UtcDateTime
        };
        user.PasswordHash = hasher.HashPassword(user, "Password1!");
        db.Users.Add(user);

        for (var i = 0; i < activeTokens; i++)
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                User = user,
                TokenHash = $"hash-{i}-{Guid.NewGuid():N}",
                ExpiresAt = FixedNow.UtcDateTime.AddDays(7),
                CreatedAt = FixedNow.UtcDateTime
            });
        }

        return user;
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"UserUnit_{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private static UserService CreateUserService(AppDbContext db, TimeProvider timeProvider, int currentUserId)
    {
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.SetupGet(c => c.UserId).Returns(currentUserId);
        var clock = new AppClock(timeProvider, AppClock.ResolveTimeZone("Asia/Ho_Chi_Minh"));

        return new UserService(
            db,
            currentUserMock.Object,
            new PasswordHasher<User>(),
            clock,
            NullLogger<UserService>.Instance);
    }
}
