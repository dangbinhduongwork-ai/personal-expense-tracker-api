using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Infrastructure.Auth;
using ExpenseTracker.Infrastructure.Data;
using ExpenseTracker.Infrastructure.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseTracker.Infrastructure;

/// <summary>
/// Dependency injection registration for persistence and external infrastructure services.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:DefaultConnection' is missing.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<DemoDataSeeder>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        return services;
    }
}

