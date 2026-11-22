namespace ExpenseTracker.Application.Entities;

/// <summary>
/// Hashed refresh token supporting rotation and reuse-theft detection.
/// </summary>
public sealed class RefreshToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? ReplacedByTokenHash { get; set; }

    // Initialized by EF Core when navigating/loading related User.
    public User User { get; set; } = null!;
}

