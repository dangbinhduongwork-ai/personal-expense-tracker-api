namespace ExpenseTracker.Api.Options;

/// <summary>
/// Options controlling optional Development-only demo dataset seeding (<c>Seed:DemoData</c>).
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool DemoData { get; init; }
}
