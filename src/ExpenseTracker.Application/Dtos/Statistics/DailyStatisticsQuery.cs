using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.Dtos.Statistics;

/// <summary>Query parameters for <c>GET /api/statistics/daily</c>.</summary>
public sealed class DailyStatisticsQuery
{
    [Required]
    public DateOnly? From { get; set; }

    [Required]
    public DateOnly? To { get; set; }
}
