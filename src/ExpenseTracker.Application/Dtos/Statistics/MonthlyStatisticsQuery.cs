using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.Dtos.Statistics;

/// <summary>Query parameters for <c>GET /api/statistics/monthly</c>.</summary>
public sealed class MonthlyStatisticsQuery
{
    [Required]
    [Range(2000, 2100)]
    public int? Year { get; set; }
}
