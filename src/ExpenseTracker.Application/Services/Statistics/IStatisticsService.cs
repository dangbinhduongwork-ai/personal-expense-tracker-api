using ExpenseTracker.Application.Dtos.Statistics;

namespace ExpenseTracker.Application.Services.Statistics;

/// <summary>
/// Application service contract for caller-scoped SQL-aggregated financial statistics.
/// </summary>
public interface IStatisticsService
{
    /// <summary>Returns income, expense, net balance, and transaction count for a period or all time.</summary>
    Task<SummaryDto> GetSummaryAsync(SummaryQuery query, CancellationToken cancellationToken);

    /// <summary>Returns zero-filled daily totals for an inclusive range up to 366 days.</summary>
    Task<IReadOnlyList<DailyStatDto>> GetDailyAsync(DailyStatisticsQuery query, CancellationToken cancellationToken);

    /// <summary>Returns zero-filled 12-month totals for a calendar year.</summary>
    Task<IReadOnlyList<MonthlyStatDto>> GetMonthlyAsync(MonthlyStatisticsQuery query, CancellationToken cancellationToken);

    /// <summary>Returns zero-filled yearly totals across up to 20 years.</summary>
    Task<IReadOnlyList<YearlyStatDto>> GetYearlyAsync(YearlyStatisticsQuery query, CancellationToken cancellationToken);

    /// <summary>Returns category totals and period percentage shares for a transaction type.</summary>
    Task<IReadOnlyList<CategoryStatDto>> GetByCategoryAsync(CategoryStatisticsQuery query, CancellationToken cancellationToken);

    /// <summary>Returns the top N categories ordered by total while preserving full-period percentages.</summary>
    Task<IReadOnlyList<CategoryStatDto>> GetTopCategoriesAsync(TopCategoriesQuery query, CancellationToken cancellationToken);

    /// <summary>Compares totals between two calendar months (defaulting to the prior month, including Jan to Dec).</summary>
    Task<MonthComparisonDto> GetComparisonAsync(ComparisonQuery query, CancellationToken cancellationToken);
}
