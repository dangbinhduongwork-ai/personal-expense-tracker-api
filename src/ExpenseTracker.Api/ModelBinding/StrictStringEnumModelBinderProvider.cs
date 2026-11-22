using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ExpenseTracker.Api.ModelBinding;

/// <summary>
/// Applies <see cref="StrictStringEnumModelBinder"/> to enum and nullable enum query parameters.
/// </summary>
public sealed class StrictStringEnumModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var underlyingType = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;
        if (!underlyingType.IsEnum)
        {
            return null;
        }

        var bindingSource = context.BindingInfo.BindingSource;
        if (bindingSource is not null && !bindingSource.CanAcceptDataFrom(BindingSource.Query))
        {
            return null;
        }

        return new StrictStringEnumModelBinder();
    }
}
