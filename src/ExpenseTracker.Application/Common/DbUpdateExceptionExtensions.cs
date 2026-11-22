using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Application.Common;

/// <summary>
/// Detects SQL Server constraint violation error numbers without referencing <c>Microsoft.Data.SqlClient</c> from Application.
/// </summary>
internal static class DbUpdateExceptionExtensions
{
    private const int SqlServerForeignKeyViolation = 547;
    private const int SqlServerUniqueIndexViolation = 2601;
    private const int SqlServerUniqueConstraintViolation = 2627;

    public static bool IsUniqueConstraintViolation(this DbUpdateException exception) =>
        HasSqlServerErrorNumber(exception, SqlServerUniqueIndexViolation, SqlServerUniqueConstraintViolation);

    public static bool IsForeignKeyConstraintViolation(this DbUpdateException exception) =>
        HasSqlServerErrorNumber(exception, SqlServerForeignKeyViolation);

    private static bool HasSqlServerErrorNumber(DbUpdateException exception, params int[] targetErrorNumbers)
    {
        var inner = exception.InnerException;
        while (inner is not null)
        {
            var numberProperty = inner.GetType().GetProperty("Number");
            if (numberProperty?.GetValue(inner) is int number && targetErrorNumbers.Contains(number))
            {
                return true;
            }

            inner = inner.InnerException;
        }

        return false;
    }
}
