using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DevalCopilot.Api.Features.Runs.GetRunEventHistory;

/// <summary>
/// Binds the optional <c>beforeSequence</c> cursor of the event-history route as the exact text it arrived as (a string, because the
/// sequence can exceed a browser number). An absent or blank value binds <see langword="null"/>; a single value passes through for the
/// query's validator to accept or refuse as a canonical positive 64-bit decimal. A repeated parameter binds <see cref="Rejected"/>,
/// which is never canonical, so it is refused rather than silently reduced to its first value.
/// </summary>
public sealed class RunEventHistoryCursorModelBinder : IModelBinder
{
    public const string Rejected = "-";

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var provided = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        bindingContext.Result = ModelBindingResult.Success(
            provided == ValueProviderResult.None || provided.Values.All(string.IsNullOrWhiteSpace) ? null
            : provided.Values.Count == 1 ? provided.FirstValue
            : Rejected);
        return Task.CompletedTask;
    }
}
