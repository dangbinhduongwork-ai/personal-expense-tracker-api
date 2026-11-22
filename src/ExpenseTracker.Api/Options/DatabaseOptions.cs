namespace ExpenseTracker.Api.Options;

/// <summary>
/// Database startup settings loaded from the <c>Database</c> configuration section.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool MigrateOnStartup { get; set; }
}

