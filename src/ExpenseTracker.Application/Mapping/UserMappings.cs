using ExpenseTracker.Application.Dtos.Users;
using ExpenseTracker.Application.Entities;

namespace ExpenseTracker.Application.Mapping;

/// <summary>
/// Manual projections between user entities and public DTOs.
/// </summary>
public static class UserMappings
{
    public static UserDto ToDto(this User user) =>
        new(user.Id, user.Email, user.FullName, user.CreatedAt);
}
