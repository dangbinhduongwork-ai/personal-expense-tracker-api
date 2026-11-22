using ExpenseTracker.Api.Options;

namespace ExpenseTracker.Api.Extensions;

/// <summary>
/// Configures named CORS policy from <c>Cors:AllowedOrigins</c>.
/// </summary>
public static class CorsExtensions
{
    public const string PolicyName = "DefaultCors";

    public static IServiceCollection AddAppCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var corsOptions = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
        var allowedOrigins = corsOptions.AllowedOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim())
            .ToArray();

        if (!environment.IsDevelopment() &&
            allowedOrigins.Any(origin => !IsSafeNonDevelopmentOrigin(origin)))
        {
            throw new InvalidOperationException(
                "Wildcard, 'null', and non-HTTP(S) CORS origins are forbidden outside Development.");
        }

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                }
            });
        });

        return services;
    }

    private static bool IsSafeNonDevelopmentOrigin(string origin)
    {
        if (string.Equals(origin, "*", StringComparison.Ordinal) ||
            string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase) ||
            origin.Contains('*', StringComparison.Ordinal))
        {
            return false;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }
}
