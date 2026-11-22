using ExpenseTracker.Api.ExceptionHandling;
using ExpenseTracker.Api.Services;
using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ExpenseTracker.Api.Extensions;

/// <summary>
/// Configures HS256 JWT Bearer authentication, <see cref="ICurrentUserService"/>, and default-deny fallback authorization.
/// </summary>
public static class AuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TimeProvider, IOptions<JwtOptions>>((options, timeProvider, jwtOptionsAccessor) =>
            {
                var jwt = jwtOptionsAccessor.Value;

                // Keep "sub" and other JWT claim names unmapped so CurrentUserService reads "sub" consistently.
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.TimeProvider = timeProvider;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(jwt.GetKeyBytes()),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    LifetimeValidator = (notBefore, expires, _, validationParameters) =>
                    {
                        if (expires is null)
                        {
                            return false;
                        }

                        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
                        var skew = validationParameters.ClockSkew;

                        if (notBefore is not null && notBefore.Value.ToUniversalTime() > utcNow + skew)
                        {
                            return false;
                        }

                        return expires.Value.ToUniversalTime() + skew > utcNow;
                    }
                };

                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await ProblemDetailsResponseWriter.WriteAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            "unauthorized",
                            "Authentication is required.");
                    },
                    OnForbidden = async context =>
                    {
                        await ProblemDetailsResponseWriter.WriteAsync(
                            context.HttpContext,
                            StatusCodes.Status403Forbidden,
                            "forbidden",
                            "You do not have permission to access this resource.");
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }
}
