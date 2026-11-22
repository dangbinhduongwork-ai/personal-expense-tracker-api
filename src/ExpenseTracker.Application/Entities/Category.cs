using ExpenseTracker.Application.Enums;

namespace ExpenseTracker.Application.Entities;

/// <summary>
/// Income or expense category. Null <see cref="UserId"/> represents a read-only system category.
/// </summary>
public sealed class Category
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public TransactionType Type { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User? User { get; set; }

    public ICollection<Transaction> Transactions { get; } = [];
}

