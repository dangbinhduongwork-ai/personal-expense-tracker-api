namespace ExpenseTracker.Application.Exceptions;

/// <summary>
/// Thrown when a resource is missing or owned by another user (masked as HTTP 404).
/// </summary>
public sealed class NotFoundException(string code, string message)
    : AppException(404, code, message);

