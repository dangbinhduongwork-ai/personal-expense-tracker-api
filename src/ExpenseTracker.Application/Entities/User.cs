namespace ExpenseTracker.Application.Entities;

/// <summary>
/// Registered application user.
/// </summary>
public sealed class User
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; } = [];

    public ICollection<Category> Categories { get; } = [];

    public ICollection<Transaction> Transactions { get; } = [];
}

