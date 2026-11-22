using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.Dtos.Auth;

/// <summary>
/// Request payload containing the opaque refresh token to rotate or revoke.
/// </summary>
public sealed class RefreshTokenRequest
{
    // Refresh tokens must never be trimmed or altered.
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string RefreshToken { get; set; } = string.Empty;
}
