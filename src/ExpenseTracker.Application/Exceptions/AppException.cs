namespace ExpenseTracker.Application.Exceptions;

/// <summary>
/// Base exception for controlled application and domain failures mapped to ProblemDetails.
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    public string Code { get; }
}

