using ExpenseTracker.Application.Exceptions;

namespace ExpenseTracker.Application.Common;

/// <summary>
/// Validates password complexity rules required by BR-12.
/// </summary>
internal static class PasswordRuleValidator
{
    public const int MinLength = 8;
    public const int MaxLength = 100;

    public static void Validate(string password, string fieldName)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength || password.Length > MaxLength)
        {
            throw new ValidationFailedException(
                fieldName,
                $"Password must be between {MinLength} and {MaxLength} characters.");
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            throw new ValidationFailedException(
                fieldName,
                "Password must contain at least one letter and at least one digit.");
        }
    }
}
