namespace ExpenseTracker.Application.Dtos.Users;

/// <summary>
/// Public representation of an authenticated user (never includes password hashes).
/// </summary>
public sealed record UserDto(
    int Id,
    string Email,
    string FullName,
    DateTime CreatedAt);
