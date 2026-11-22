using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Common;
using ExpenseTracker.Application.Dtos.Auth;
using ExpenseTracker.Application.Dtos.Users;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Exceptions;
using ExpenseTracker.Application.Mapping;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Application.Services.Auth;

/// <summary>
/// Implements account registration, timing-safe credential verification, and rotating refresh tokens.
/// </summary>
public sealed class AuthService(
    IAppDbContext dbContext,
    ITokenService tokenService,
    IPasswordHasher<User> passwordHasher,
    ICurrentUserService currentUserService,
    IAppClock clock,
    ILogger<AuthService> logger) : IAuthService
{
    private static readonly User DummyUser = new() { Email = "timing-parity@invalid.local" };
    private static readonly string DummyPasswordHash =
        new PasswordHasher<User>().HashPassword(DummyUser, "TimingParity_Placeholder_1!");

    /// <inheritdoc />
    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        PasswordRuleValidator.Validate(request.Password, "password");

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        // Emails are persisted normalized to lowercase, allowing SQL Server to seek UX_Users_Email directly.
        var exists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (exists)
        {
            throw new ConflictException("email_already_registered", "An account with this email already exists.");
        }

        var utcNow = clock.UtcNow;
        var user = new User
        {
            Email = normalizedEmail,
            FullName = request.FullName.Trim(),
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            throw new ConflictException("email_already_registered", "An account with this email already exists.");
        }

        logger.LogInformation("Registered user {UserId}", user.Id);
        return user.ToDto();
    }

    /// <inheritdoc />
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        // Direct equality preserves Index Seek on UX_Users_Email instead of forcing a LOWER() scan.
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null)
        {
            // Execute constant-work PBKDF2 verification on unknown emails so timing does not leak email existence (SEC-19).
            passwordHasher.VerifyHashedPassword(DummyUser, DummyPasswordHash, request.Password);
            throw new UnauthorizedException("invalid_credentials", "Invalid email or password.");
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            throw new UnauthorizedException("invalid_credentials", "Invalid email or password.");
        }

        var utcNow = clock.UtcNow;
        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            user.UpdatedAt = utcNow;
        }

        logger.LogInformation("Authenticated user {UserId}", user.Id);
        return await IssueTokenPairAsync(user, utcNow, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var utcNow = clock.UtcNow;
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);

        var existingToken = await dbContext.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

        if (existingToken is null)
        {
            throw new UnauthorizedException("refresh_token_invalid", "Refresh token is invalid.");
        }

        // Check revocation before expiry so presenting any revoked token revokes the entire token family.
        if (existingToken.RevokedAt is not null)
        {
            logger.LogWarning("Revoked refresh token reuse detected for user {UserId}", existingToken.UserId);
            await dbContext.RevokeAllActiveTokensForUserAsync(existingToken.UserId, utcNow, cancellationToken);
            throw new UnauthorizedException("refresh_token_invalid", "Refresh token has been revoked.");
        }

        if (existingToken.ExpiresAt <= utcNow)
        {
            throw new UnauthorizedException("refresh_token_invalid", "Refresh token has expired.");
        }

        var newRefresh = tokenService.CreateRefreshToken();
        existingToken.RevokedAt = utcNow;
        existingToken.ReplacedByTokenHash = newRefresh.TokenHash;

        var accessToken = tokenService.CreateAccessToken(existingToken.User);
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = existingToken.UserId,
            TokenHash = newRefresh.TokenHash,
            ExpiresAt = newRefresh.ExpiresAt,
            CreatedAt = utcNow
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another concurrent request already rotated this token; clear stale tracked state and revoke all active sessions.
            logger.LogWarning("Concurrent refresh token contention detected for user {UserId}", existingToken.UserId);
            dbContext.ClearChangeTracker();
            await dbContext.RevokeAllActiveTokensForUserAsync(existingToken.UserId, utcNow, cancellationToken);
            throw new UnauthorizedException("refresh_token_invalid", "Refresh token has been revoked.");
        }

        logger.LogInformation("Rotated refresh token for user {UserId}", existingToken.UserId);
        return new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAt,
            newRefresh.RawToken,
            newRefresh.ExpiresAt,
            existingToken.User.ToDto());
    }

    /// <inheritdoc />
    public async Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);

        // Scope strictly by caller UserId so a caller can never revoke or probe another user's token.
        var token = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash && rt.UserId == userId, cancellationToken);

        if (token is null || token.RevokedAt is not null)
        {
            return;
        }

        token.RevokedAt = clock.UtcNow;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Concurrent logout, refresh, or password change already revoked this token; stay idempotent.
            dbContext.ClearChangeTracker();
            return;
        }

        logger.LogInformation("Logged out refresh token for user {UserId}", userId);
    }

    private async Task<AuthResponse> IssueTokenPairAsync(
        User user,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var accessToken = tokenService.CreateAccessToken(user);
        var refreshToken = tokenService.CreateRefreshToken();

        dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshToken.TokenHash,
            ExpiresAt = refreshToken.ExpiresAt,
            CreatedAt = utcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAt,
            refreshToken.RawToken,
            refreshToken.ExpiresAt,
            user.ToDto());
    }
}
