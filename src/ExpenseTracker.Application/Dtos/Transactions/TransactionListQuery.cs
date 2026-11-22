using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Application.Enums;

namespace ExpenseTracker.Application.Dtos.Transactions;

/// <summary>
/// Query parameters for filtering, searching, sorting, and paging transactions.
/// </summary>
public sealed class TransactionListQuery
{
    private string? _search;
    private string? _sortBy;
    private string? _sortDir;

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    [EnumDataType(typeof(TransactionType))]
    public TransactionType? Type { get; set; }

    public int? CategoryId { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    [StringLength(100)]
    public string? Search
    {
        get => _search;
        set => _search = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public string? SortBy
    {
        get => _sortBy;
        set => _sortBy = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public string? SortDir
    {
        get => _sortDir;
        set => _sortDir = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
