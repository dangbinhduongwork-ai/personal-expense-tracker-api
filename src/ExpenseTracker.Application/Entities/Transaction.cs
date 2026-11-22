using ExpenseTracker.Application.Enums;

namespace ExpenseTracker.Application.Entities;

/// <summary>
/// Financial transaction belonging to a single user and category.
/// </summary>
public sealed class Transaction
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int CategoryId { get; set; }

    public string Title { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public TransactionType Type { get; set; }

    public string? Description { get; set; }

    public DateOnly TransactionDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Initialized by EF Core when navigating/loading related User.
    public User User { get; set; } = null!;

    // Initialized by EF Core when navigating/loading related Category.
    public Category Category { get; set; } = null!;
}

