using ExpenseTracker.Api.ExceptionHandling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using ApiOpenApiOptions = ExpenseTracker.Api.Options.OpenApiOptions;

namespace ExpenseTracker.Api.Extensions;

/// <summary>
/// Registers .NET 10 OpenAPI generation with JWT Bearer security metadata and Swagger UI gated behind <c>OpenApi:EnableUi</c>.
/// </summary>
public static class OpenApiExtensions
{
    public static IServiceCollection AddOpenApiDocs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ApiOpenApiOptions>()
            .Bind(configuration.GetSection(ApiOpenApiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<BearerSecurityOperationTransformer>();
        });

        return services;
    }

    public static WebApplication UseOpenApiSwaggerUi(this WebApplication app)
    {
        var openApiOptions = app.Services.GetRequiredService<IOptions<ApiOpenApiOptions>>().Value;
        if (!openApiOptions.EnableUi)
        {
            return app;
        }

        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "Expense Tracker API v1");
        });

        return app;
    }

    public static WebApplication MapOpenApiDocs(this WebApplication app)
    {
        var openApiOptions = app.Services.GetRequiredService<IOptions<ApiOpenApiOptions>>().Value;
        if (openApiOptions.EnableUi)
        {
            app.MapOpenApi().AllowAnonymous();
            return app;
        }

        // Explicitly emit 404 ProblemDetails anonymously when disabled so FallbackPolicy does not return 401 on SEC-25 checks.
        app.Map("/swagger/{**rest}", (HttpContext ctx) =>
            ProblemDetailsResponseWriter.WriteAsync(ctx, StatusCodes.Status404NotFound, "not_found", "Resource was not found.", ctx.RequestAborted))
            .AllowAnonymous();
        app.Map("/openapi/{**rest}", (HttpContext ctx) =>
            ProblemDetailsResponseWriter.WriteAsync(ctx, StatusCodes.Status404NotFound, "not_found", "Resource was not found.", ctx.RequestAborted))
            .AllowAnonymous();
        return app;
    }

    private sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
    {
        public Task TransformAsync(
            OpenApiDocument document,
            OpenApiDocumentTransformerContext context,
            CancellationToken cancellationToken)
        {
            document.Info.Title = "Personal Expense Tracker API";
            document.Info.Version = "v1";
            document.Info.Description = "RESTful API for personal income, expenses, categories, and financial statistics.";
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter the JWT access token issued by /api/auth/login or /api/auth/refresh-token."
            };

            return Task.CompletedTask;
        }
    }

    private sealed class BearerSecurityOperationTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(
            OpenApiOperation operation,
            OpenApiOperationTransformerContext context,
            CancellationToken cancellationToken)
        {
            var isAnonymous = context.Description.ActionDescriptor.EndpointMetadata
                .OfType<IAllowAnonymous>()
                .Any();

            if (isAnonymous)
            {
                return Task.CompletedTask;
            }

            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
            });

            return Task.CompletedTask;
        }
    }
}
