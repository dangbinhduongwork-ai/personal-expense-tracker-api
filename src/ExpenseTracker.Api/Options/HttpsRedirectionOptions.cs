namespace ExpenseTracker.Api.Options;

/// <summary>
/// Controls HTTPS redirection and HSTS outside Development (disabled for local HTTP container demos and test hosts).
/// </summary>
public sealed class HttpsRedirectionOptions
{
    public const string SectionName = "HttpsRedirection";

    public bool Enabled { get; set; } = true;
}

