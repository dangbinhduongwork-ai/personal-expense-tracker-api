using ExpenseTracker.Application.Dtos.Statistics;
using ExpenseTracker.Application.Services.Statistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Api.Controllers;

/// <summary>
/// Read-only endpoints for caller-scoped income, expense, and trend statistics.
/// </summary>
[ApiController]
[Route("api/statistics")]
[Authorize]
public sealed class StatisticsController(IStatisticsService statisticsService) : ControllerBase
{
    /// <summary>Returns income, expense, balance, and transaction count for a month, year, or all time.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(SummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SummaryDto>> GetSummary(
        [FromQuery] SummaryQuery query,
        CancellationToken cancellationToken) =>
        Ok(await statisticsService.GetSummaryAsync(query, cancellationToken));

    /// <summary>Returns zero-filled daily statistics over an inclusive range up to 366 days.</summary>
    [HttpGet("daily")]
    [ProducesResponseType(typeof(IReadOnlyList<DailyStatDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<DailyStatDto>>> GetDaily(
        [FromQuery] DailyStatisticsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await statisticsService.GetDailyAsync(query, cancellationToken));

    /// <summary>Returns zero-filled 12-month statistics for a calendar year.</summary>
    [HttpGet("monthly")]
    [ProducesResponseType(typeof(IReadOnlyList<MonthlyStatDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<MonthlyStatDto>>> GetMonthly(
        [FromQuery] MonthlyStatisticsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await statisticsService.GetMonthlyAsync(query, cancellationToken));

    /// <summary>Returns zero-filled yearly statistics across up to 20 years.</summary>
    [HttpGet("yearly")]
    [ProducesResponseType(typeof(IReadOnlyList<YearlyStatDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<YearlyStatDto>>> GetYearly(
        [FromQuery] YearlyStatisticsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await statisticsService.GetYearlyAsync(query, cancellationToken));

    /// <summary>Returns per-category totals and percentage shares for a transaction type and optional date range.</summary>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryStatDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<CategoryStatDto>>> GetByCategory(
        [FromQuery] CategoryStatisticsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await statisticsService.GetByCategoryAsync(query, cancellationToken));

    /// <summary>Returns the top N categories while preserving each category's percentage share of the full period total.</summary>
    [HttpGet("top-categories")]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryStatDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<CategoryStatDto>>> GetTopCategories(
        [FromQuery] TopCategoriesQuery query,
        CancellationToken cancellationToken) =>
        Ok(await statisticsService.GetTopCategoriesAsync(query, cancellationToken));

    /// <summary>Compares income, expense, and balance between two months with percentage deltas.</summary>
    [HttpGet("comparison")]
    [ProducesResponseType(typeof(MonthComparisonDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MonthComparisonDto>> GetComparison(
        [FromQuery] ComparisonQuery query,
        CancellationToken cancellationToken) =>
        Ok(await statisticsService.GetComparisonAsync(query, cancellationToken));
}
