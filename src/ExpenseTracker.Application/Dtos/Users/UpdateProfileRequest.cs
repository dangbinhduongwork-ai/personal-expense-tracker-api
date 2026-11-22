using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.Dtos.Users;

/// <summary>
/// Request payload for updating the signed-in user's display name.
/// </summary>
public sealed class UpdateProfileRequest
{
    private string _fullName = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string FullName
    {
        get => _fullName;
        set => _fullName = value?.Trim() ?? string.Empty;
    }
}
