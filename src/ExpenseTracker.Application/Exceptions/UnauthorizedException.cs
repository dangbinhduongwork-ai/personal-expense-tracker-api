namespace ExpenseTracker.Application.Exceptions;

/// <summary>
/// Thrown when authentication credentials, tokens, or caller claims are invalid (HTTP 401).
/// </summary>
public sealed class UnauthorizedException(string code, string message)
    : AppException(401, code, message);

