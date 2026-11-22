using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExpenseTracker.Api.ExceptionHandling;
using ExpenseTracker.Api.Extensions;
using ExpenseTracker.Api.Health;
using ExpenseTracker.Api.ModelBinding;
using ExpenseTracker.Api.Options;
using ExpenseTracker.Application;
using ExpenseTracker.Infrastructure;
using ExpenseTracker.Infrastructure.Data;
using ExpenseTracker.Infrastructure.Data.Seed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<AppOptions>()
    .Bind(builder.Configuration.GetSection(AppOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<HttpsRedirectionOptions>()
    .Bind(builder.Configuration.GetSection(HttpsRedirectionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SeedOptions>()
    .Bind(builder.Configuration.GetSection(SeedOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);

var configuredTimeZoneId = builder.Configuration[$"{AppOptions.SectionName}:{nameof(AppOptions.TimeZoneId)}"]
    ?? "Asia/Ho_Chi_Minh";
builder.Services.AddApplication(configuredTimeZoneId);
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers(options =>
    {
        options.ModelBinderProviders.Insert(0, new StrictStringEnumModelBinderProvider());
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var problemDetails = new ValidationProblemDetails(context.ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Type = "https://httpstatuses.io/400",
                Detail = "One or more validation errors occurred."
            };
            problemDetails.Extensions["code"] = "validation_failed";
            problemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

            return new BadRequestObjectResult(problemDetails)
            {
                ContentTypes = { "application/problem+json" }
            };
        };
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
    });

// Align minimal/OpenAPI HTTP JSON options with MVC controllers so enums emit as strings in /openapi/v1.json.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
});

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var statusCode = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
        context.ProblemDetails.Type ??= $"https://httpstatuses.io/{statusCode}";
        context.ProblemDetails.Detail ??= ReasonPhrases.GetReasonPhrase(statusCode);
        context.ProblemDetails.Extensions.TryAdd(
            "code",
            ProblemDetailsResponseWriter.ResolveDefaultCode(statusCode));
        context.ProblemDetails.Extensions.TryAdd(
            "traceId",
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
    };
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddAppCors(builder.Configuration, builder.Environment);
builder.Services.AddAuthRateLimiting(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddOpenApiDocs(builder.Configuration);

var app = builder.Build();

var databaseOptions = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
if (databaseOptions.MigrateOnStartup)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}

var seedOptions = app.Services.GetRequiredService<IOptions<SeedOptions>>().Value;
if (app.Environment.IsDevelopment() && seedOptions.DemoData)
{
    await using var scope = app.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
    await seeder.SeedAsync();
}

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Task.CompletedTask;
    });
    await next();
});

app.UseExceptionHandler();

app.UseStatusCodePages(async statusCodeContext =>
{
    var httpContext = statusCodeContext.HttpContext;
    var statusCode = httpContext.Response.StatusCode;
    var code = ProblemDetailsResponseWriter.ResolveDefaultCode(statusCode);
    var detail = ReasonPhrases.GetReasonPhrase(statusCode);
    await ProblemDetailsResponseWriter.WriteAsync(
        httpContext,
        statusCode,
        code,
        string.IsNullOrEmpty(detail) ? "The request could not be completed." : detail,
        httpContext.RequestAborted);
});

var httpsOptions = app.Services.GetRequiredService<IOptions<HttpsRedirectionOptions>>().Value;
if (!app.Environment.IsDevelopment() && httpsOptions.Enabled)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors(CorsExtensions.PolicyName);
app.UseOpenApiSwaggerUi();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapOpenApiDocs();

await app.RunAsync();

/// <summary>
/// Exposed for integration testing via <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
