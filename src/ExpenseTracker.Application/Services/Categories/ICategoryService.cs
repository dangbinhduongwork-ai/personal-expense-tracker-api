using ExpenseTracker.Application.Dtos.Categories;
using ExpenseTracker.Application.Enums;

namespace ExpenseTracker.Application.Services.Categories;

/// <summary>
/// Manages system and caller-owned personal categories.
/// </summary>
public interface ICategoryService
{
    /// <summary>
    /// Lists all visible system and caller-owned categories, optionally filtered by transaction type.
    /// </summary>
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(TransactionType? type, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a visible system or caller-owned category by id.
    /// </summary>
    Task<CategoryDto> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new personal category owned by the authenticated caller.
    /// </summary>
    Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Updates a caller-owned personal category after checking ownership, conflicts, and usage rules.
    /// </summary>
    Task<CategoryDto> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an unused caller-owned personal category.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
