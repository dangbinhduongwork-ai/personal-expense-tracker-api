using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.Dtos.Statistics;

/// <summary>Query parameters for <c>GET /api/statistics/comparison</c>.</summary>
public sealed class ComparisonQuery
{
    [Required]
    [Range(2000, 2100)]
    public int? Year { get; set; }

    [Required]
    [Range(1, 12)]
    public int? Month { get; set; }

    [Range(2000, 2100)]
    public int? CompareToYear { get; set; }

    [Range(1, 12)]
    public int? CompareToMonth { get; set; }
}
