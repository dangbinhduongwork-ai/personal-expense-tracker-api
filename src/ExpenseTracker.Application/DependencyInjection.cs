using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Common;
using ExpenseTracker.Application.Entities;
using ExpenseTracker.Application.Services.Auth;
using ExpenseTracker.Application.Services.Categories;
using ExpenseTracker.Application.Services.Statistics;
using ExpenseTracker.Application.Services.Transactions;
using ExpenseTracker.Application.Services.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseTracker.Application;

/// <summary>
/// Dependency injection registration for Application layer services and clock helpers.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, string timeZoneId)
    {
        var resolvedTimeZone = AppClock.ResolveTimeZone(timeZoneId);
        services.AddSingleton(resolvedTimeZone);
        services.AddSingleton<IAppClock>(sp =>
            new AppClock(sp.GetRequiredService<TimeProvider>(), resolvedTimeZone));

        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IStatisticsService, StatisticsService>();

        return services;
    }
}

