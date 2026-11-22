using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Infrastructure.Auth;

/// <summary>
/// Strongly typed JWT settings validated at startup via <c>ValidateDataAnnotations().ValidateOnStart()</c>.
/// </summary>
public sealed class JwtOptions : IValidatableObject
{
    public const string SectionName = "Jwt";
    public const int MinimumKeyBytes = 32;

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    [Required]
    public string Key { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 7;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!TryDecodeKey(out var keyBytes) || keyBytes.Length < MinimumKeyBytes)
        {
            yield return new ValidationResult(
                $"Jwt:Key must be valid Base64 decoding to at least {MinimumKeyBytes} bytes.",
                [nameof(Key)]);
        }
    }

    public byte[] GetKeyBytes()
    {
        if (!TryDecodeKey(out var keyBytes) || keyBytes.Length < MinimumKeyBytes)
        {
            throw new InvalidOperationException(
                $"Jwt:Key must be valid Base64 decoding to at least {MinimumKeyBytes} bytes.");
        }

        return keyBytes;
    }

    private bool TryDecodeKey(out byte[] keyBytes)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            keyBytes = [];
            return false;
        }

        try
        {
            keyBytes = Convert.FromBase64String(Key);
            return true;
        }
        catch (FormatException)
        {
            keyBytes = [];
            return false;
        }
    }
}
