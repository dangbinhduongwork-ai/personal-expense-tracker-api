using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExpenseTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts <see cref="Program"/> in-memory against the test SQL Server connection string.
/// </summary>
public sealed class ApiFactory(
    string connectionString,
    bool openApiEnableUi = true,
    string environmentName = "Development",
    TimeProvider? timeProvider = null,
    Action<IServiceCollection>? configureExtraServices = null,
    bool migrateOnStartup = true,
    string? jwtKeyOverride = null,
    int rateLimitPermitLimit = 1000,
    bool seedDemoData = false) : WebApplicationFactory<Program>
{
    public const string DefaultJwtIssuer = "ExpenseTracker.TestIssuer";
    public const string DefaultJwtAudience = "ExpenseTracker.TestAudience";

    public string JwtSigningKey { get; } = jwtKeyOverride
        ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", migrateOnStartup ? "true" : "false");
        builder.UseSetting("Seed:DemoData", seedDemoData ? "true" : "false");
        builder.UseSetting("OpenApi:EnableUi", openApiEnableUi ? "true" : "false");
        builder.UseSetting("HttpsRedirection:Enabled", "false");
        builder.UseSetting("App:TimeZoneId", "Asia/Ho_Chi_Minh");
        builder.UseSetting("Jwt:Issuer", DefaultJwtIssuer);
        builder.UseSetting("Jwt:Audience", DefaultJwtAudience);
        builder.UseSetting("Jwt:Key", JwtSigningKey);
        builder.UseSetting("RateLimiting:PermitLimit", rateLimitPermitLimit.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimiting:WindowSeconds", "60");

        builder.ConfigureServices(services =>
        {
            if (timeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(timeProvider);
            }

            configureExtraServices?.Invoke(services);
        });
    }
}
