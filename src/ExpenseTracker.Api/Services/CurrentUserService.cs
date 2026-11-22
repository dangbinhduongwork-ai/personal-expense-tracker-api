using System.Globalization;
using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Exceptions;

namespace ExpenseTracker.Api.Services;

/// <summary>
/// Resolves the caller's numeric user id exclusively from the validated JWT <c>sub</c> claim.
/// </summary>
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public int UserId
    {
        get
        {
            var subClaim = httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value;
            if (int.TryParse(subClaim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId) && userId > 0)
            {
                return userId;
            }

            throw new UnauthorizedException("unauthorized", "Authentication is required.");
        }
    }
}
