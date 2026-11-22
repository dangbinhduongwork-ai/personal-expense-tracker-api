using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.Dtos.Auth;

/// <summary>
/// Credentials submitted to authenticate and receive an access/refresh token pair.
/// </summary>
public sealed class LoginRequest
{
    private string _email = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email
    {
        get => _email;
        set => _email = value?.Trim() ?? string.Empty;
    }

    // Passwords must never be trimmed.
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Password { get; set; } = string.Empty;
}
