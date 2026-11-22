using System.Reflection;
using ExpenseTracker.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExpenseTracker.IntegrationTests.Conventions;

public sealed class ApiConventionsTests
{
    private static readonly string[] ExpectedAnonymousActions =
    [
        $"{nameof(AuthController)}.{nameof(AuthController.Login)}",
        $"{nameof(AuthController)}.{nameof(AuthController.RefreshToken)}",
        $"{nameof(AuthController)}.{nameof(AuthController.Register)}"
    ];

    [Fact]
    public void AllControllerActions_HaveAuthorizationOrExactAnonymousAllowList()
    {
        var actions = GetAllControllerActions();
        Assert.NotEmpty(actions);

        var actualAnonymous = new List<string>();

        foreach (var (controller, action) in actions)
        {
            var isAnonymous = action.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
            var hasAuthorize = action.GetCustomAttribute<AuthorizeAttribute>() is not null
                || controller.GetCustomAttribute<AuthorizeAttribute>() is not null;

            Assert.True(
                isAnonymous || hasAuthorize,
                $"Action {controller.Name}.{action.Name} must declare [Authorize] or [AllowAnonymous].");

            if (isAnonymous)
            {
                actualAnonymous.Add($"{controller.Name}.{action.Name}");
            }
        }

        actualAnonymous.Sort(StringComparer.Ordinal);
        Assert.Equal(ExpectedAnonymousActions, actualAnonymous.ToArray());
    }

    [Fact]
    public void AllControllerActions_DeclareRequiredProducesResponseTypes()
    {
        var actions = GetAllControllerActions();

        foreach (var (controller, action) in actions)
        {
            var produces = action.GetCustomAttributes<ProducesResponseTypeAttribute>().ToList();
            var statusCodes = produces.Select(p => p.StatusCode).ToHashSet();

            Assert.True(
                statusCodes.Any(code => code is >= 200 and < 300),
                $"{controller.Name}.{action.Name} must declare at least one 2xx [ProducesResponseType].");

            var isAnonymous = action.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
            if (!isAnonymous)
            {
                Assert.True(
                    statusCodes.Contains(401),
                    $"Protected action {controller.Name}.{action.Name} must declare [ProducesResponseType(401)].");
            }

            if (action.GetCustomAttribute<EnableRateLimitingAttribute>() is not null)
            {
                Assert.True(
                    statusCodes.Contains(429),
                    $"Rate-limited action {controller.Name}.{action.Name} must declare [ProducesResponseType(429)].");
            }
        }
    }

    private static List<(Type Controller, MethodInfo Action)> GetAllControllerActions()
    {
        var apiAssembly = typeof(AuthController).Assembly;
        var controllers = apiAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t))
            .ToList();

        var result = new List<(Type, MethodInfo)>();
        foreach (var controller in controllers)
        {
            var methods = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null);

            foreach (var method in methods)
            {
                result.Add((controller, method));
            }
        }

        return result;
    }
}
