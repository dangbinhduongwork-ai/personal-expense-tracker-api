using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.Dtos.Statistics;

/// <summary>Query parameters for <c>GET /api/statistics/summary</c>.</summary>
public sealed class SummaryQuery
{
    [Range(2000, 2100)]
    public int? Year { get; set; }

    [Range(1, 12)]
    public int? Month { get; set; }
}
