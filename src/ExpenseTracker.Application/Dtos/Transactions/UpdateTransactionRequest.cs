using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Application.Enums;

namespace ExpenseTracker.Application.Dtos.Transactions;

/// <summary>
/// Request payload for full replacement of an existing transaction via PUT.
/// </summary>
public sealed class UpdateTransactionRequest
{
    private string? _title;
    private string? _description;

    [Required(AllowEmptyStrings = false)]
    [StringLength(200, MinimumLength = 1)]
    public string? Title
    {
        get => _title;
        set => _title = value?.Trim();
    }

    [Required]
    [Range(typeof(decimal), "0.01", "1000000000000", ParseLimitsInInvariantCulture = true)]
    public decimal? Amount { get; set; }

    [Required]
    [EnumDataType(typeof(TransactionType))]
    public TransactionType? Type { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int? CategoryId { get; set; }

    [StringLength(1000)]
    public string? Description
    {
        get => _description;
        set => _description = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [Required]
    public DateOnly? TransactionDate { get; set; }
}
