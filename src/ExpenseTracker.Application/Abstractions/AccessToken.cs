namespace ExpenseTracker.Application.Abstractions;

/// <summary>
/// Signed JWT access token and its UTC expiration timestamp.
/// </summary>
public sealed record AccessToken(string Token, DateTime ExpiresAt);

