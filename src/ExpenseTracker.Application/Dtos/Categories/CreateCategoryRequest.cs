using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Application.Enums;

namespace ExpenseTracker.Application.Dtos.Categories;

/// <summary>
/// Request payload for creating a personal income or expense category.
/// </summary>
public sealed class CreateCategoryRequest
{
    private string _name = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name
    {
        get => _name;
        set => _name = value?.Trim() ?? string.Empty;
    }

    [Required]
    [EnumDataType(typeof(TransactionType))]
    public TransactionType? Type { get; set; }
}
