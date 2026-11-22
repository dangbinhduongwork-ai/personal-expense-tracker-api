using ExpenseTracker.Application.Dtos.Auth;
using ExpenseTracker.Application.Dtos.Users;

namespace ExpenseTracker.Application.Services.Auth;

/// <summary>
/// Handles user registration, login, refresh-token rotation, and logout.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Registers a new user account with a normalized email and hashed password.
    /// </summary>
    Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Authenticates a user by email and password and issues an access/refresh token pair.
    /// </summary>
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Rotates a valid refresh token into a new token pair or revokes the family on replay.
    /// </summary>
    Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Idempotently revokes a caller-owned refresh token.
    /// </summary>
    Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken);
}
