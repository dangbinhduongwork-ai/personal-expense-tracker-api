using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ExpenseTracker.Api.ModelBinding;

/// <summary>
/// Rejects numeric, duplicate-key, empty-invalid, or comma-separated enum query values while preserving
/// case-insensitive named enum parsing.
/// </summary>
public sealed class StrictStringEnumModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueProviderResult);

        if (valueProviderResult.Values.Count > 1)
        {
            bindingContext.ModelState.AddModelError(
                bindingContext.ModelName,
                $"Only one value is allowed for {bindingContext.ModelName}.");
            return Task.CompletedTask;
        }

        var rawValue = valueProviderResult.FirstValue;
        var nullableUnderlying = Nullable.GetUnderlyingType(bindingContext.ModelType);
        var enumType = nullableUnderlying ?? bindingContext.ModelType;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            if (nullableUnderlying is not null)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }
            else
            {
                bindingContext.ModelState.AddModelError(
                    bindingContext.ModelName,
                    $"The {bindingContext.ModelName} field is required.");
            }

            return Task.CompletedTask;
        }

        var trimmed = rawValue.Trim();

        // Reject numeric literals (e.g. "0", "1", "-1") and multi-value flag strings ("Income,Expense").
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ||
            trimmed.Contains(',', StringComparison.Ordinal) ||
            !Enum.TryParse(enumType, trimmed, ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(enumType, parsed))
        {
            var allowed = string.Join(", ", Enum.GetNames(enumType));
            bindingContext.ModelState.AddModelError(
                bindingContext.ModelName,
                $"The value '{rawValue}' is invalid for {bindingContext.ModelName}. Allowed values: {allowed}.");
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(parsed);
        return Task.CompletedTask;
    }
}
