namespace ExpenseTracker.Application.Exceptions;

/// <summary>
/// Thrown when service-level validation produces field errors mapped to ValidationProblemDetails (HTTP 400).
/// </summary>
public sealed class ValidationFailedException : AppException
{
    public ValidationFailedException(
        IReadOnlyDictionary<string, string[]> errors,
        string message = "One or more validation errors occurred.")
        : base(400, "validation_failed", message)
    {
        Errors = errors;
    }

    public ValidationFailedException(string field, string errorMessage)
        : this(new Dictionary<string, string[]> { [field] = [errorMessage] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

