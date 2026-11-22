using ExpenseTracker.Application.Dtos.Users;

namespace ExpenseTracker.Application.Services.Users;

/// <summary>
/// Manages the authenticated user's profile and password.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Returns the authenticated caller's profile.
    /// </summary>
    Task<UserDto> GetMeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Updates the authenticated caller's display profile.
    /// </summary>
    Task<UserDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies the caller's current password, sets a new password hash, and revokes active refresh tokens.
    /// </summary>
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken);
}
