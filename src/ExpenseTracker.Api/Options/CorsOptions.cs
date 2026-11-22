namespace ExpenseTracker.Api.Options;

/// <summary>
/// CORS policy origins loaded from <c>Cors</c>. Empty array disables cross-origin requests.
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}

