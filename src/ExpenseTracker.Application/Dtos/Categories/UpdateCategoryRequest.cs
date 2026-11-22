using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Application.Enums;

namespace ExpenseTracker.Application.Dtos.Categories;

/// <summary>
/// Request payload for full-replacing a personal category's name and type.
/// </summary>
public sealed class UpdateCategoryRequest
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
