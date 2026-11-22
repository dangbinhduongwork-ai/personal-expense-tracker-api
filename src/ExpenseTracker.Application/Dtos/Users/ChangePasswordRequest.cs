using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.Dtos.Users;

/// <summary>
/// Request payload for changing the caller's password.
/// </summary>
public sealed class ChangePasswordRequest
{
    // Passwords must never be trimmed.
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;
}
