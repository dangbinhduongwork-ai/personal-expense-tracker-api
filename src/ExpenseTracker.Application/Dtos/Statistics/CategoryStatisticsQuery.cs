using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Application.Enums;

namespace ExpenseTracker.Application.Dtos.Statistics;

/// <summary>Query parameters for <c>GET /api/statistics/categories</c>.</summary>
public sealed class CategoryStatisticsQuery
{
    [Required]
    [EnumDataType(typeof(TransactionType))]
    public TransactionType? Type { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }
}
