using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.Dtos.Auth;

/// <summary>
/// Request payload for registering a new user account.
/// </summary>
public sealed class RegisterRequest
{
    private string _email = string.Empty;
    private string _fullName = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email
    {
        get => _email;
        set => _email = value?.Trim() ?? string.Empty;
    }

    // Passwords must never be trimmed so spaces remain significant if supplied.
    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string FullName
    {
        get => _fullName;
        set => _fullName = value?.Trim() ?? string.Empty;
    }
}
