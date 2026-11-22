namespace ExpenseTracker.Api.Options;

/// <summary>
/// OpenAPI and Swagger UI exposure settings loaded from <c>OpenApi</c>.
/// </summary>
public sealed class OpenApiOptions
{
    public const string SectionName = "OpenApi";

    public bool EnableUi { get; set; }
}

