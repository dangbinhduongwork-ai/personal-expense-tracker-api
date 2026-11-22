using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Application.Enums;

namespace ExpenseTracker.Application.Dtos.Statistics;

/// <summary>Query parameters for <c>GET /api/statistics/top-categories</c>.</summary>
public sealed class TopCategoriesQuery
{
    [Required]
    [EnumDataType(typeof(TransactionType))]
    public TransactionType? Type { get; set; }

    [Range(1, 20)]
    public int Limit { get; set; } = 5;

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }
}
