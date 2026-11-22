using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Common;
using ExpenseTracker.Application.Dtos.Categories;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Enums;
using ExpenseTracker.Application.Exceptions;
using ExpenseTracker.Application.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Application.Services.Categories;

/// <summary>
/// Implements category queries and ownership-scoped personal category CRUD rules (BR-07 through BR-10).
/// </summary>
public sealed class CategoryService(
    IAppDbContext dbContext,
    ICurrentUserService currentUserService,
    IAppClock clock,
    ILogger<CategoryService> logger) : ICategoryService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(
        TransactionType? type,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;

        var query = dbContext.Categories
            .AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId);

        if (type is not null)
        {
            query = query.Where(c => c.Type == type.Value);
        }

        var categories = await query
            .Select(c => new CategoryDto(c.Id, c.Name, c.Type, c.UserId == null, c.CreatedAt))
            .ToListAsync(cancellationToken);

        // Sort the caller's small category list in memory to avoid LOWER() in SQL ORDER BY
        // and guarantee identical ordering across SQL Server and EF InMemory.
        return categories
            .OrderByDescending(c => c.IsSystem)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Type)
            .ThenBy(c => c.Id)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<CategoryDto> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;

        var category = await dbContext.Categories
            .AsNoTracking()
            .Where(c => c.Id == id && (c.UserId == null || c.UserId == userId))
            .Select(c => new CategoryDto(c.Id, c.Name, c.Type, c.UserId == null, c.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return category ?? throw new NotFoundException("category_not_found", $"Category {id} was not found.");
    }

    /// <inheritdoc />
    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        var targetType = ValidateType(request.Type);
        var trimmedName = ValidateName(request.Name);

        await EnsureCategoryNameAvailableAsync(userId, targetType, trimmedName, excludeCategoryId: null, cancellationToken);

        var utcNow = clock.UtcNow;
        var category = new Category
        {
            UserId = userId,
            Name = trimmedName,
            Type = targetType,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        dbContext.Categories.Add(category);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            throw new ConflictException(
                "category_name_exists",
                $"A {targetType} category named '{trimmedName}' already exists.");
        }

        logger.LogInformation("Created category {CategoryId} for user {UserId}", category.Id, userId);
        return category.ToDto();
    }

    /// <inheritdoc />
    public async Task<CategoryDto> UpdateAsync(
        int id,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        var targetType = ValidateType(request.Type);
        var trimmedName = ValidateName(request.Name);

        // Order matters (404 -> 403 -> 409): never expose conflict checks on system or foreign categories.
        var category = await LoadMutablePersonalCategoryAsync(id, userId, cancellationToken);

        if (category.Type != targetType)
        {
            var usageCount = await dbContext.Transactions
                .CountAsync(t => t.UserId == userId && t.CategoryId == id, cancellationToken);

            if (usageCount > 0)
            {
                throw new ConflictException(
                    "category_in_use",
                    $"Category is used by {usageCount} transaction(s) and its type cannot be changed.");
            }
        }

        await EnsureCategoryNameAvailableAsync(userId, targetType, trimmedName, excludeCategoryId: id, cancellationToken);

        category.Name = trimmedName;
        category.Type = targetType;
        category.UpdatedAt = clock.UtcNow;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            throw new ConflictException(
                "category_name_exists",
                $"A {targetType} category named '{trimmedName}' already exists.");
        }

        logger.LogInformation("Updated category {CategoryId} for user {UserId}", category.Id, userId);
        return category.ToDto();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        var category = await LoadMutablePersonalCategoryAsync(id, userId, cancellationToken);

        var usageCount = await dbContext.Transactions
            .CountAsync(t => t.UserId == userId && t.CategoryId == id, cancellationToken);

        if (usageCount > 0)
        {
            throw new ConflictException(
                "category_in_use",
                $"Category is used by {usageCount} transaction(s) and cannot be deleted.");
        }

        dbContext.Categories.Remove(category);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsForeignKeyConstraintViolation())
        {
            throw new ConflictException(
                "category_in_use",
                "Category is used by one or more transactions and cannot be deleted.");
        }

        logger.LogInformation("Deleted category {CategoryId} for user {UserId}", id, userId);
    }

    private async Task<Category> LoadMutablePersonalCategoryAsync(
        int id,
        int userId,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories
            .FirstOrDefaultAsync(c => c.Id == id && (c.UserId == null || c.UserId == userId), cancellationToken)
            ?? throw new NotFoundException("category_not_found", $"Category {id} was not found.");

        if (category.UserId is null)
        {
            throw new ForbiddenException(
                "system_category_readonly",
                "System categories are read-only and cannot be modified or deleted.");
        }

        return category;
    }

    private async Task EnsureCategoryNameAvailableAsync(
        int userId,
        TransactionType type,
        string trimmedName,
        int? excludeCategoryId,
        CancellationToken cancellationToken)
    {
        // Query via leading index keys (UserId, Type) and check case-insensitivity in memory so SQL avoids
        // LOWER() scans and matches EF InMemory; UX_Categories_UserId_Type_Name enforces DB-level uniqueness.
        var existingNames = await dbContext.Categories
            .AsNoTracking()
            .Where(c => (c.UserId == null || c.UserId == userId) && c.Type == type)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);

        var duplicateExists = existingNames.Any(c =>
            (excludeCategoryId is null || c.Id != excludeCategoryId.Value) &&
            string.Equals(c.Name, trimmedName, StringComparison.OrdinalIgnoreCase));

        if (duplicateExists)
        {
            throw new ConflictException(
                "category_name_exists",
                $"A {type} category named '{trimmedName}' already exists.");
        }
    }

    private static TransactionType ValidateType(TransactionType? type)
    {
        if (type is null || !Enum.IsDefined(type.Value))
        {
            throw new ValidationFailedException("type", "Category type must be Income or Expense.");
        }

        return type.Value;
    }

    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > 100)
        {
            throw new ValidationFailedException("name", "Category name must be between 1 and 100 characters.");
        }

        return trimmed;
    }
}
