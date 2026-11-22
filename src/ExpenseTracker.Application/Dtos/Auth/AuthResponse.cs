using ExpenseTracker.Application.Dtos.Users;

namespace ExpenseTracker.Application.Dtos.Auth;

/// <summary>
/// Authentication response containing short-lived JWT access token, rotating refresh token, and caller profile.
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserDto User);
