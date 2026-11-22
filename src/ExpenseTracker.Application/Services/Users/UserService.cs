using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Common;
using ExpenseTracker.Application.Dtos.Users;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Exceptions;
using ExpenseTracker.Application.Mapping;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Application.Services.Users;

/// <summary>
/// Implements self-service profile queries, profile updates, and password changes scoped to <see cref="ICurrentUserService"/>.
/// </summary>
public sealed class UserService(
    IAppDbContext dbContext,
    ICurrentUserService currentUserService,
    IPasswordHasher<User> passwordHasher,
    IAppClock clock,
    ILogger<UserService> logger) : IUserService
{
    /// <inheritdoc />
    public async Task<UserDto> GetMeAsync(CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        var user = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserDto(u.Id, u.Email, u.FullName, u.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return user ?? throw new UnauthorizedException("unauthorized", "Authenticated user no longer exists.");
    }

    /// <inheritdoc />
    public async Task<UserDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            throw new UnauthorizedException("unauthorized", "Authenticated user no longer exists.");
        }

        user.FullName = request.FullName.Trim();
        user.UpdatedAt = clock.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Updated profile for user {UserId}", userId);
        return user.ToDto();
    }

    /// <inheritdoc />
    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        PasswordRuleValidator.Validate(request.NewPassword, "newPassword");

        if (string.Equals(request.NewPassword, request.CurrentPassword, StringComparison.Ordinal))
        {
            throw new ValidationFailedException("newPassword", "New password must differ from the current password.");
        }

        var userId = currentUserService.UserId;
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            throw new UnauthorizedException("unauthorized", "Authenticated user no longer exists.");
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.CurrentPassword);

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            throw new BusinessRuleException("current_password_incorrect", "Current password is incorrect.");
        }

        var utcNow = clock.UtcNow;
        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.UpdatedAt = utcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.RevokeAllActiveTokensForUserAsync(userId, utcNow, cancellationToken);
        logger.LogInformation("Changed password and revoked active refresh tokens for user {UserId}", userId);
    }
}
