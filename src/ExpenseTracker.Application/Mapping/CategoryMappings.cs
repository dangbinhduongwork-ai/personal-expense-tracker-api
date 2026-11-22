using ExpenseTracker.Application.Dtos.Categories;
using ExpenseTracker.Application.Entities;

namespace ExpenseTracker.Application.Mapping;

/// <summary>
/// Manual projections between category entities and DTOs.
/// </summary>
public static class CategoryMappings
{
    public static CategoryDto ToDto(this Category category) =>
        new(
            category.Id,
            category.Name,
            category.Type,
            category.UserId is null,
            category.CreatedAt);
}
