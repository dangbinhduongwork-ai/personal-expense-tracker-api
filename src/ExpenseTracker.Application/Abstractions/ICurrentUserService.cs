namespace ExpenseTracker.Application.Abstractions;

/// <summary>
/// Resolves the authenticated caller's user id exclusively from the validated JWT claims.
/// </summary>
public interface ICurrentUserService
{
    int UserId { get; }
}

