using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.Dtos.Statistics;

/// <summary>Query parameters for <c>GET /api/statistics/yearly</c>.</summary>
public sealed class YearlyStatisticsQuery
{
    [Range(2000, 2100)]
    public int? FromYear { get; set; }

    [Range(2000, 2100)]
    public int? ToYear { get; set; }
}
