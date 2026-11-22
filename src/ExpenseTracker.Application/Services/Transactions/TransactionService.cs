using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Common;
using ExpenseTracker.Application.Dtos.Common;
using ExpenseTracker.Application.Dtos.Transactions;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Enums;
using ExpenseTracker.Application.Exceptions;
using ExpenseTracker.Application.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Application.Services.Transactions;

/// <summary>
/// Implements caller-scoped transaction CRUD, filtering, searching, whitelist sorting,
/// and deterministic paging (TXN-01..09, BR-01..06, BR-15).
/// </summary>
public sealed class TransactionService(
    IAppDbContext dbContext,
    ICurrentUserService currentUserService,
    IAppClock clock,
    ILogger<TransactionService> logger) : ITransactionService
{
    public const decimal MaxAmount = 1_000_000_000_000m;
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 1000;
    public const int MaxSearchLength = 100;
    public const int MaxPageSize = 100;
    public static readonly DateOnly MinTransactionDate = new(2000, 1, 1);

    /// <inheritdoc />
    public async Task<PagedResult<TransactionDto>> GetAllAsync(
        TransactionListQuery query,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        var validated = ValidateListQuery(query);

        var filteredQuery = BuildFilteredQuery(userId, query, validated.NormalizedSearch);

        var totalCount = await filteredQuery.CountAsync(cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (totalCount + validated.PageSize - 1) / validated.PageSize;

        var skip = (long)(validated.Page - 1) * validated.PageSize;
        if (totalCount == 0 || skip >= totalCount)
        {
            return new PagedResult<TransactionDto>([], validated.Page, validated.PageSize, totalCount, totalPages);
        }

        var orderedQuery = ApplySorting(filteredQuery, validated.SortBy, validated.Ascending);

        var items = await orderedQuery
            .Skip((int)skip)
            .Take(validated.PageSize)
            .Select(t => new TransactionDto(
                t.Id,
                t.Title,
                t.Amount,
                t.Type,
                t.CategoryId,
                t.Category.Name,
                t.Description,
                t.TransactionDate,
                t.CreatedAt,
                t.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<TransactionDto>(items, validated.Page, validated.PageSize, totalCount, totalPages);
    }

    /// <inheritdoc />
    public async Task<TransactionDto> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;

        // Ownership filter executes in SQL in a single query so foreign ids behave identically to missing ones.
        var transaction = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Id == id && t.UserId == userId)
            .Select(t => new TransactionDto(
                t.Id,
                t.Title,
                t.Amount,
                t.Type,
                t.CategoryId,
                t.Category.Name,
                t.Description,
                t.TransactionDate,
                t.CreatedAt,
                t.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return transaction
            ?? throw new NotFoundException("transaction_not_found", $"Transaction {id} was not found.");
    }

    /// <inheritdoc />
    public async Task<TransactionDto> CreateAsync(
        CreateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        var input = ValidateWriteFields(
            request.Title,
            request.Amount,
            request.Type,
            request.CategoryId,
            request.Description,
            request.TransactionDate);

        var category = await ResolveUsableCategoryAsync(userId, input.CategoryId, input.Type, cancellationToken);

        var utcNow = clock.UtcNow;
        var entity = new Transaction
        {
            UserId = userId,
            CategoryId = category.Id,
            Title = input.Title,
            Amount = input.Amount,
            Type = input.Type,
            Description = input.Description,
            TransactionDate = input.TransactionDate,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        dbContext.Transactions.Add(entity);
        await SaveChangesWithCategoryCheckAsync(category.Id, cancellationToken);

        logger.LogInformation("Created transaction {TransactionId} for user {UserId}", entity.Id, userId);
        return entity.ToDto(category.Name);
    }

    /// <inheritdoc />
    public async Task<TransactionDto> UpdateAsync(
        int id,
        UpdateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;

        // Verify ownership first (404) before checking category or field validity (400) so callers cannot probe foreign rows.
        var entity = await GetOwnedEntityOrThrowAsync(userId, id, cancellationToken);

        var input = ValidateWriteFields(
            request.Title,
            request.Amount,
            request.Type,
            request.CategoryId,
            request.Description,
            request.TransactionDate);

        var category = await ResolveUsableCategoryAsync(userId, input.CategoryId, input.Type, cancellationToken);

        entity.Title = input.Title;
        entity.Amount = input.Amount;
        entity.Type = input.Type;
        entity.CategoryId = category.Id;
        entity.Description = input.Description;
        entity.TransactionDate = input.TransactionDate;
        entity.UpdatedAt = clock.UtcNow;

        await SaveChangesWithCategoryCheckAsync(category.Id, cancellationToken);

        logger.LogInformation("Updated transaction {TransactionId} for user {UserId}", entity.Id, userId);
        return entity.ToDto(category.Name);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        var entity = await GetOwnedEntityOrThrowAsync(userId, id, cancellationToken);

        dbContext.Transactions.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Deleted transaction {TransactionId} for user {UserId}", id, userId);
    }

    private IQueryable<Transaction> BuildFilteredQuery(
        int userId,
        TransactionListQuery query,
        string? normalizedSearch)
    {
        var filtered = dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId);

        if (query.Type is { } type)
        {
            filtered = filtered.Where(t => t.Type == type);
        }

        if (query.CategoryId is { } categoryId)
        {
            filtered = filtered.Where(t => t.CategoryId == categoryId);
        }

        if (query.From is { } from)
        {
            filtered = filtered.Where(t => t.TransactionDate >= from);
        }

        if (query.To is { } to)
        {
            filtered = filtered.Where(t => t.TransactionDate <= to);
        }

        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            // EF Core 10 translates parameterized Contains to escaped LIKE ... ESCAPE N'\' on SQL Server
            // (so %, _, [, and \ match literally); .ToLower() ensures EF InMemory parity in unit tests.
            var searchLower = normalizedSearch.ToLowerInvariant();
            filtered = filtered.Where(t => t.Title.ToLower().Contains(searchLower));
        }

        return filtered;
    }

    private async Task SaveChangesWithCategoryCheckAsync(int categoryId, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsForeignKeyConstraintViolation())
        {
            throw new BusinessRuleException(
                "category_not_found",
                $"Category {categoryId} was not found or is not accessible.");
        }
    }

    private static IOrderedQueryable<Transaction> ApplySorting(
        IQueryable<Transaction> query,
        string sortBy,
        bool ascending)
    {
        // Always finish with an Id tie-breaker matching sort direction so paging stays stable across identical values.
        return (sortBy, ascending) switch
        {
            ("amount", true) => query.OrderBy(t => t.Amount).ThenBy(t => t.Id),
            ("amount", false) => query.OrderByDescending(t => t.Amount).ThenByDescending(t => t.Id),
            ("title", true) => query.OrderBy(t => t.Title).ThenBy(t => t.Id),
            ("title", false) => query.OrderByDescending(t => t.Title).ThenByDescending(t => t.Id),
            (_, true) => query.OrderBy(t => t.TransactionDate).ThenBy(t => t.Id),
            (_, false) => query.OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id)
        };
    }

    private async Task<Transaction> GetOwnedEntityOrThrowAsync(
        int userId,
        int id,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken);

        return entity
            ?? throw new NotFoundException("transaction_not_found", $"Transaction {id} was not found.");
    }

    private async Task<Category> ResolveUsableCategoryAsync(
        int userId,
        int categoryId,
        TransactionType expectedType,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Id == categoryId && (c.UserId == null || c.UserId == userId),
                cancellationToken);

        if (category is null)
        {
            throw new BusinessRuleException(
                "category_not_found",
                $"Category {categoryId} was not found or is not accessible.");
        }

        if (category.Type != expectedType)
        {
            throw new BusinessRuleException(
                "category_type_mismatch",
                $"Category '{category.Name}' is {category.Type} and cannot be used for an {expectedType} transaction.");
        }

        return category;
    }

    private ValidatedWriteInput ValidateWriteFields(
        string? rawTitle,
        decimal? rawAmount,
        TransactionType? rawType,
        int? rawCategoryId,
        string? rawDescription,
        DateOnly? rawTransactionDate)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var title = rawTitle?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title) || title.Length > MaxTitleLength)
        {
            errors["title"] = [$"Title is required and must be between 1 and {MaxTitleLength} characters."];
        }

        if (rawAmount is null)
        {
            errors["amount"] = ["Amount is required."];
        }
        else if (rawAmount.Value <= 0m || rawAmount.Value > MaxAmount || decimal.Round(rawAmount.Value, 2) != rawAmount.Value)
        {
            errors["amount"] = [$"Amount must be > 0, <= {MaxAmount}, and have at most 2 decimal places."];
        }

        if (rawType is null || !Enum.IsDefined(rawType.Value))
        {
            errors["type"] = ["Type must be Income or Expense."];
        }

        if (rawCategoryId is null || rawCategoryId.Value <= 0)
        {
            errors["categoryId"] = ["CategoryId must be a positive integer."];
        }

        var description = string.IsNullOrWhiteSpace(rawDescription) ? null : rawDescription.Trim();
        if (description is not null && description.Length > MaxDescriptionLength)
        {
            errors["description"] = [$"Description must be {MaxDescriptionLength} characters or fewer."];
        }

        var maxAllowedDate = clock.Today.AddDays(1);
        if (rawTransactionDate is null || rawTransactionDate.Value == default)
        {
            errors["transactionDate"] = ["TransactionDate is required."];
        }
        else if (rawTransactionDate.Value < MinTransactionDate || rawTransactionDate.Value > maxAllowedDate)
        {
            errors["transactionDate"] = [$"TransactionDate must be between {MinTransactionDate:yyyy-MM-dd} and {maxAllowedDate:yyyy-MM-dd}."];
        }

        if (errors.Count > 0 ||
            rawAmount is not { } amount ||
            rawType is not { } type ||
            rawCategoryId is not { } categoryId ||
            rawTransactionDate is not { } transactionDate)
        {
            throw new ValidationFailedException(errors);
        }

        return new ValidatedWriteInput(
            title,
            amount,
            type,
            categoryId,
            description,
            transactionDate);
    }

    private static ValidatedListQuery ValidateListQuery(TransactionListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (query.Page < 1)
        {
            errors["page"] = ["Page must be greater than or equal to 1."];
        }

        if (query.PageSize < 1 || query.PageSize > MaxPageSize)
        {
            errors["pageSize"] = [$"PageSize must be between 1 and {MaxPageSize}."];
        }

        if (query.Type is not null && !Enum.IsDefined(query.Type.Value))
        {
            errors["type"] = ["Type must be Income or Expense."];
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (normalizedSearch is not null && normalizedSearch.Length > MaxSearchLength)
        {
            errors["search"] = [$"Search must be {MaxSearchLength} characters or fewer."];
        }

        var sortBy = (string.IsNullOrWhiteSpace(query.SortBy) ? "date" : query.SortBy.Trim()).ToLowerInvariant();
        if (sortBy is not ("date" or "amount" or "title"))
        {
            errors["sortBy"] = ["SortBy must be one of: date, amount, title."];
        }

        var sortDir = (string.IsNullOrWhiteSpace(query.SortDir) ? "desc" : query.SortDir.Trim()).ToLowerInvariant();
        if (sortDir is not ("asc" or "desc"))
        {
            errors["sortDir"] = ["SortDir must be either asc or desc."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        if (query.From is not null && query.To is not null && query.From.Value > query.To.Value)
        {
            throw new BusinessRuleException(
                "invalid_date_range",
                "The 'from' date must be earlier than or equal to the 'to' date.");
        }

        return new ValidatedListQuery(
            query.Page,
            query.PageSize,
            normalizedSearch,
            sortBy,
            sortDir == "asc");
    }

    private readonly record struct ValidatedWriteInput(
        string Title,
        decimal Amount,
        TransactionType Type,
        int CategoryId,
        string? Description,
        DateOnly TransactionDate);

    private readonly record struct ValidatedListQuery(
        int Page,
        int PageSize,
        string? NormalizedSearch,
        string SortBy,
        bool Ascending);
}
