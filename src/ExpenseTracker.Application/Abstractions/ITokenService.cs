using ExpenseTracker.Application.Entities;

namespace ExpenseTracker.Application.Abstractions;

/// <summary>
/// Issues JWT access tokens and cryptographically random, SHA-256 hashed refresh tokens.
/// </summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(User user);

    RefreshTokenValue CreateRefreshToken();

    string HashRefreshToken(string rawToken);
}

