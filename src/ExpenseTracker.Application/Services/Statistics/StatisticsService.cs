using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Dtos.Statistics;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Enums;
using ExpenseTracker.Application.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Application.Services.Statistics;

/// <summary>
/// Computes caller-scoped financial statistics via SQL-side aggregation with zero-filling
/// and <see cref="MidpointRounding.AwayFromZero"/> percentage rounding.
/// </summary>
public sealed class StatisticsService(
    IAppDbContext dbContext,
    ICurrentUserService currentUserService,
    IAppClock clock) : IStatisticsService
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;
    public const int MaxDailyDays = 366;
    public const int MaxYearlySpan = 20;
    public const int MaxTopCategoriesLimit = 20;

    /// <inheritdoc />
    public async Task<SummaryDto> GetSummaryAsync(SummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var userId = currentUserService.UserId;
        var (from, to) = ResolveSummaryBounds(query);

        var grouped = await ApplyDateBounds(BaseQuery(userId), from, to)
            .GroupBy(t => t.Type)
            .Select(g => new
            {
                Type = g.Key,
                Total = g.Sum(t => t.Amount),
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);

        var income = 0m;
        var expense = 0m;
        var count = 0;

        foreach (var row in grouped)
        {
            count += row.Count;
            if (row.Type == TransactionType.Income)
            {
                income = row.Total;
            }
            else if (row.Type == TransactionType.Expense)
            {
                expense = row.Total;
            }
        }

        return new SummaryDto(from, to, income, expense, income - expense, count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailyStatDto>> GetDailyAsync(
        DailyStatisticsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var userId = currentUserService.UserId;

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.From is null)
        {
            errors["from"] = ["From date is required."];
        }

        if (query.To is null)
        {
            errors["to"] = ["To date is required."];
        }

        if (errors.Count > 0 || query.From is not { } from || query.To is not { } to)
        {
            throw new ValidationFailedException(errors);
        }

        if (from > to)
        {
            throw new BusinessRuleException(
                "invalid_date_range",
                "The 'from' date must be earlier than or equal to the 'to' date.");
        }

        var totalDays = to.DayNumber - from.DayNumber + 1;
        if (totalDays > MaxDailyDays)
        {
            throw new BusinessRuleException(
                "invalid_date_range",
                $"Daily statistics range cannot exceed {MaxDailyDays} inclusive days.");
        }

        var rows = await ApplyDateBounds(BaseQuery(userId), from, to)
            .GroupBy(t => new { t.TransactionDate, t.Type })
            .Select(g => new
            {
                g.Key.TransactionDate,
                g.Key.Type,
                Total = g.Sum(t => t.Amount)
            })
            .ToListAsync(cancellationToken);

        var totalsByDate = new Dictionary<DateOnly, (decimal Income, decimal Expense)>();
        foreach (var row in rows)
        {
            totalsByDate.TryGetValue(row.TransactionDate, out var current);
            totalsByDate[row.TransactionDate] = row.Type == TransactionType.Income
                ? (row.Total, current.Expense)
                : (current.Income, row.Total);
        }

        // Zero-fill every calendar day in [from, to] in memory after SQL aggregation.
        var result = new List<DailyStatDto>(totalDays);
        for (var offset = 0; offset < totalDays; offset++)
        {
            var date = from.AddDays(offset);
            totalsByDate.TryGetValue(date, out var pair);
            result.Add(new DailyStatDto(date, pair.Income, pair.Expense, pair.Income - pair.Expense));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MonthlyStatDto>> GetMonthlyAsync(
        MonthlyStatisticsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var userId = currentUserService.UserId;
        var year = ValidateRequiredYear(query.Year, "year");

        // Use SARGable inclusive date bounds so SQL Server can seek IX_Transactions_UserId_TransactionDate.
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);

        var rows = await ApplyDateBounds(BaseQuery(userId), from, to)
            .GroupBy(t => new { t.TransactionDate.Month, t.Type })
            .Select(g => new
            {
                g.Key.Month,
                g.Key.Type,
                Total = g.Sum(t => t.Amount)
            })
            .ToListAsync(cancellationToken);

        var totalsByMonth = new Dictionary<int, (decimal Income, decimal Expense)>();
        foreach (var row in rows)
        {
            totalsByMonth.TryGetValue(row.Month, out var current);
            totalsByMonth[row.Month] = row.Type == TransactionType.Income
                ? (row.Total, current.Expense)
                : (current.Income, row.Total);
        }

        var result = new List<MonthlyStatDto>(12);
        for (var month = 1; month <= 12; month++)
        {
            totalsByMonth.TryGetValue(month, out var pair);
            result.Add(new MonthlyStatDto(year, month, pair.Income, pair.Expense, pair.Income - pair.Expense));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<YearlyStatDto>> GetYearlyAsync(
        YearlyStatisticsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var userId = currentUserService.UserId;

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.FromYear is { } fy && (fy < MinYear || fy > MaxYear))
        {
            errors["fromYear"] = [$"FromYear must be between {MinYear} and {MaxYear}."];
        }

        if (query.ToYear is { } ty && (ty < MinYear || ty > MaxYear))
        {
            errors["toYear"] = [$"ToYear must be between {MinYear} and {MaxYear}."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        var toYear = query.ToYear ?? clock.Today.Year;
        var fromYear = query.FromYear ?? Math.Max(MinYear, toYear - 4);

        if (fromYear > toYear)
        {
            throw new BusinessRuleException(
                "invalid_date_range",
                "The 'fromYear' value must be less than or equal to 'toYear'.");
        }

        var span = toYear - fromYear + 1;
        if (span > MaxYearlySpan)
        {
            throw new BusinessRuleException(
                "invalid_date_range",
                $"Yearly statistics span cannot exceed {MaxYearlySpan} inclusive years.");
        }

        var from = new DateOnly(fromYear, 1, 1);
        var to = new DateOnly(toYear, 12, 31);

        var rows = await ApplyDateBounds(BaseQuery(userId), from, to)
            .GroupBy(t => new { t.TransactionDate.Year, t.Type })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Type,
                Total = g.Sum(t => t.Amount)
            })
            .ToListAsync(cancellationToken);

        var totalsByYear = new Dictionary<int, (decimal Income, decimal Expense)>();
        foreach (var row in rows)
        {
            totalsByYear.TryGetValue(row.Year, out var current);
            totalsByYear[row.Year] = row.Type == TransactionType.Income
                ? (row.Total, current.Expense)
                : (current.Income, row.Total);
        }

        var result = new List<YearlyStatDto>(span);
        for (var year = fromYear; year <= toYear; year++)
        {
            totalsByYear.TryGetValue(year, out var pair);
            result.Add(new YearlyStatDto(year, pair.Income, pair.Expense, pair.Income - pair.Expense));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategoryStatDto>> GetByCategoryAsync(
        CategoryStatisticsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var userId = currentUserService.UserId;
        var targetType = ValidateRequiredType(query.Type);
        ValidateOptionalDateRange(query.From, query.To);

        return await GetCategoryBreakdownAsync(userId, targetType, query.From, query.To, limit: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategoryStatDto>> GetTopCategoriesAsync(
        TopCategoriesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var userId = currentUserService.UserId;

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.Type is null || !Enum.IsDefined(query.Type.Value))
        {
            errors["type"] = ["Type must be Income or Expense."];
        }

        if (query.Limit < 1 || query.Limit > MaxTopCategoriesLimit)
        {
            errors["limit"] = [$"Limit must be between 1 and {MaxTopCategoriesLimit}."];
        }

        if (errors.Count > 0 || query.Type is not { } targetType)
        {
            throw new ValidationFailedException(errors);
        }

        ValidateOptionalDateRange(query.From, query.To);

        return await GetCategoryBreakdownAsync(
            userId,
            targetType,
            query.From,
            query.To,
            query.Limit,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MonthComparisonDto> GetComparisonAsync(
        ComparisonQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var userId = currentUserService.UserId;

        var (year, month, prevYear, prevMonth) = ResolveComparisonPeriods(query);

        // Execute sequentially on a single scoped DbContext because EF Core DbContext is not thread-safe.
        var current = await GetPeriodTotalsAsync(userId, year, month, cancellationToken);
        var previous = await GetPeriodTotalsAsync(userId, prevYear, prevMonth, cancellationToken);

        var incomeChange = current.Income - previous.Income;
        var expenseChange = current.Expense - previous.Expense;
        var balanceChange = current.Balance - previous.Balance;

        decimal? incomeChangePercent = previous.Income == 0m
            ? null
            : CalculatePercent(incomeChange, previous.Income);

        decimal? expenseChangePercent = previous.Expense == 0m
            ? null
            : CalculatePercent(expenseChange, previous.Expense);

        return new MonthComparisonDto(
            current,
            previous,
            incomeChange,
            expenseChange,
            balanceChange,
            incomeChangePercent,
            expenseChangePercent);
    }

    private IQueryable<Transaction> BaseQuery(int userId) =>
        dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId);

    private static IQueryable<Transaction> ApplyDateBounds(
        IQueryable<Transaction> query,
        DateOnly? from,
        DateOnly? to)
    {
        if (from is { } start)
        {
            query = query.Where(t => t.TransactionDate >= start);
        }

        if (to is { } end)
        {
            query = query.Where(t => t.TransactionDate <= end);
        }

        return query;
    }

    private async Task<IReadOnlyList<CategoryStatDto>> GetCategoryBreakdownAsync(
        int userId,
        TransactionType targetType,
        DateOnly? from,
        DateOnly? to,
        int? limit,
        CancellationToken cancellationToken)
    {
        var grouped = await ApplyDateBounds(BaseQuery(userId), from, to)
            .Where(t => t.Type == targetType)
            .GroupBy(t => new { t.CategoryId, CategoryName = t.Category.Name })
            .Select(g => new
            {
                g.Key.CategoryId,
                g.Key.CategoryName,
                Total = g.Sum(t => t.Amount),
                TransactionCount = g.Count()
            })
            .ToListAsync(cancellationToken);

        if (grouped.Count == 0)
        {
            return [];
        }

        var fullPeriodTotal = grouped.Sum(g => g.Total);
        if (fullPeriodTotal == 0m)
        {
            return [];
        }

        // Percentage stays relative to the full period Type total even when taking Top-N.
        var ordered = grouped
            .OrderByDescending(g => g.Total)
            .ThenBy(g => g.CategoryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.CategoryId)
            .Select(g => new CategoryStatDto(
                g.CategoryId,
                g.CategoryName,
                targetType,
                g.Total,
                g.TransactionCount,
                CalculatePercent(g.Total, fullPeriodTotal)));

        return limit is { } take ? ordered.Take(take).ToList() : ordered.ToList();
    }

    private async Task<PeriodTotalsDto> GetPeriodTotalsAsync(
        int userId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var (from, to) = GetMonthBounds(year, month);

        var grouped = await ApplyDateBounds(BaseQuery(userId), from, to)
            .GroupBy(t => t.Type)
            .Select(g => new
            {
                Type = g.Key,
                Total = g.Sum(t => t.Amount)
            })
            .ToListAsync(cancellationToken);

        var income = 0m;
        var expense = 0m;
        foreach (var row in grouped)
        {
            if (row.Type == TransactionType.Income)
            {
                income = row.Total;
            }
            else if (row.Type == TransactionType.Expense)
            {
                expense = row.Total;
            }
        }

        return new PeriodTotalsDto(year, month, income, expense, income - expense);
    }

    private static (DateOnly From, DateOnly To) GetMonthBounds(int year, int month) =>
        (new DateOnly(year, month, 1), new DateOnly(year, month, DateTime.DaysInMonth(year, month)));

    public static decimal CalculatePercent(decimal part, decimal whole) =>
        Math.Round(part / whole * 100m, 2, MidpointRounding.AwayFromZero);

    private static (DateOnly? From, DateOnly? To) ResolveSummaryBounds(SummaryQuery query)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (query.Month is not null && query.Year is null)
        {
            errors["year"] = ["Year is required when Month is specified."];
        }

        if (query.Year is { } y && (y < MinYear || y > MaxYear))
        {
            errors["year"] = [$"Year must be between {MinYear} and {MaxYear}."];
        }

        if (query.Month is { } m && (m < 1 || m > 12))
        {
            errors["month"] = ["Month must be between 1 and 12."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        if (query.Year is { } year && query.Month is { } month)
        {
            var (start, end) = GetMonthBounds(year, month);
            return (start, end);
        }

        if (query.Year is { } yearOnly)
        {
            return (new DateOnly(yearOnly, 1, 1), new DateOnly(yearOnly, 12, 31));
        }

        return (null, null);
    }

    private static int ValidateRequiredYear(int? rawYear, string fieldName)
    {
        if (rawYear is null || rawYear.Value < MinYear || rawYear.Value > MaxYear)
        {
            throw new ValidationFailedException(fieldName, $"Year must be between {MinYear} and {MaxYear}.");
        }

        return rawYear.Value;
    }

    private static TransactionType ValidateRequiredType(TransactionType? rawType)
    {
        if (rawType is null || !Enum.IsDefined(rawType.Value))
        {
            throw new ValidationFailedException("type", "Type must be Income or Expense.");
        }

        return rawType.Value;
    }

    private static void ValidateOptionalDateRange(DateOnly? from, DateOnly? to)
    {
        if (from is { } start && to is { } end && start > end)
        {
            throw new BusinessRuleException(
                "invalid_date_range",
                "The 'from' date must be earlier than or equal to the 'to' date.");
        }
    }

    private static (int Year, int Month, int PrevYear, int PrevMonth) ResolveComparisonPeriods(ComparisonQuery query)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (query.Year is null || query.Year.Value < MinYear || query.Year.Value > MaxYear)
        {
            errors["year"] = [$"Year must be between {MinYear} and {MaxYear}."];
        }

        if (query.Month is null || query.Month.Value < 1 || query.Month.Value > 12)
        {
            errors["month"] = ["Month must be between 1 and 12."];
        }

        if (query.CompareToYear.HasValue != query.CompareToMonth.HasValue)
        {
            var missingKey = query.CompareToYear.HasValue ? "compareToMonth" : "compareToYear";
            errors[missingKey] = ["Both compareToYear and compareToMonth must be provided together."];
        }

        if (query.CompareToYear is { } cy && (cy < MinYear || cy > MaxYear))
        {
            errors["compareToYear"] = [$"CompareToYear must be between {MinYear} and {MaxYear}."];
        }

        if (query.CompareToMonth is { } cm && (cm < 1 || cm > 12))
        {
            errors["compareToMonth"] = ["CompareToMonth must be between 1 and 12."];
        }

        if (errors.Count > 0 || query.Year is not { } year || query.Month is not { } month)
        {
            throw new ValidationFailedException(errors);
        }

        var (prevYear, prevMonth) = query.CompareToYear is { } customYear && query.CompareToMonth is { } customMonth
            ? (customYear, customMonth)
            : month == 1
                ? (year - 1, 12)
                : (year, month - 1);

        return (year, month, prevYear, prevMonth);
    }
}
