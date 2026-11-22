namespace ExpenseTracker.Application.Dtos.Common;

/// <summary>
/// Standard paged response container for list endpoints.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
