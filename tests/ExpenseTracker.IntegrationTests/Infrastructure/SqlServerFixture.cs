using ExpenseTracker.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace ExpenseTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Provides an isolated SQL Server database instance per test collection using Testcontainers
/// (or <c>TEST_SQL_CONNECTION</c> when provided in CI/local environments).
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    private MsSqlContainer? _container;
    private string _baseConnectionString = string.Empty;

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var externalConnectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION");

        if (string.IsNullOrWhiteSpace(externalConnectionString))
        {
            _container = new MsSqlBuilder(SqlServerImage).Build();
            await _container.StartAsync();
            _baseConnectionString = _container.GetConnectionString();
        }
        else
        {
            _baseConnectionString = externalConnectionString;
        }

        ConnectionString = CreateDatabaseConnectionString($"ExpenseTrackerTest_{Guid.NewGuid():N}");

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public string CreateDatabaseConnectionString(string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(_baseConnectionString)
        {
            InitialCatalog = databaseName
        };

        return builder.ConnectionString;
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new AppDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

