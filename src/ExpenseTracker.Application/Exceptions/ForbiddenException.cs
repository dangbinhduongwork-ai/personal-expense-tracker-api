namespace ExpenseTracker.Application.Exceptions;

/// <summary>
/// Thrown when an authenticated caller attempts an action forbidden by policy (e.g., modifying system categories, HTTP 403).
/// </summary>
public sealed class ForbiddenException(string code, string message)
    : AppException(403, code, message);

