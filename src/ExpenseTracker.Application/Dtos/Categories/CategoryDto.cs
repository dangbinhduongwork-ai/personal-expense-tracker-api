using ExpenseTracker.Application.Enums;

namespace ExpenseTracker.Application.Dtos.Categories;

/// <summary>
/// Public representation of a system or personal category (never exposes owner <c>UserId</c>).
/// </summary>
public sealed record CategoryDto(
    int Id,
    string Name,
    TransactionType Type,
    bool IsSystem,
    DateTime CreatedAt);
