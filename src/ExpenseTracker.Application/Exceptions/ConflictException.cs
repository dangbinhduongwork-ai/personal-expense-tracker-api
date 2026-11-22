namespace ExpenseTracker.Application.Exceptions;

/// <summary>
/// Thrown when a write violates uniqueness or referential state rules (HTTP 409).
/// </summary>
public sealed class ConflictException(string code, string message)
    : AppException(409, code, message);

