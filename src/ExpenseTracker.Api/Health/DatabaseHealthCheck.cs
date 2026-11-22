using ExpenseTracker.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ExpenseTracker.Api.Health;

/// <summary>
/// Checks SQL Server database connectivity for the anonymous <c>/health</c> endpoint.
/// </summary>
public sealed class DatabaseHealthCheck(IAppDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var canConnect = await dbContext.CanConnectAsync(cancellationToken);
        return canConnect
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Database connection check failed.");
    }
}

