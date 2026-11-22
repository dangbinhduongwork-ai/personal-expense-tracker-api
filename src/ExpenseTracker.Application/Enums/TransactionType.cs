namespace ExpenseTracker.Application.Enums;

/// <summary>
/// Classifies a transaction or category as income or expense.
/// Explicit numbering starts at 1 so default value 0 can never pass silently as a valid enum.
/// </summary>
public enum TransactionType
{
    Income = 1,
    Expense = 2
}

