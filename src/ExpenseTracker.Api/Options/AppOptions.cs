using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Api.Options;

/// <summary>
/// General application settings loaded from the <c>App</c> configuration section.
/// </summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    [Required]
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
}

