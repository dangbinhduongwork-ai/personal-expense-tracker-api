namespace ExpenseTracker.Application.Abstractions;

/// <summary>
/// Newly generated opaque refresh token along with its SHA-256 hash and UTC expiration.
/// </summary>
public sealed record RefreshTokenValue(string RawToken, string TokenHash, DateTime ExpiresAt);

