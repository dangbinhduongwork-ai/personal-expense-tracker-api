namespace ExpenseTracker.Application.Exceptions;

/// <summary>
/// Thrown when a business rule fails without field-level model errors (HTTP 400).
/// </summary>
public sealed class BusinessRuleException(string code, string message)
    : AppException(400, code, message);

