using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DevalCopilot.Api.Features.Projects.GetProjectRunHistory;

/// <summary>
/// Binds the optional numeric query scalars of the run-history route (the public parameters stay <c>int?</c>). An absent or empty
/// value binds <see langword="null"/>, exactly as the framework's own integer binder does, and a whole 32-bit integer binds as itself.
/// A present value that is anything else (text, a fraction, an overflow) binds <see cref="Rejected"/> and adds no model-state entry,
/// so the framework never writes its own error body that repeats the rejected value: the query's validator refuses it and the shared
/// Problem Details contract answers, without the value.
/// </summary>
public sealed class RunHistoryScalarModelBinder : IModelBinder
{
    /// <summary>Outside the accepted range of both scalars (a positive cursor, a limit of 1 to 20), so it is always refused.</summary>
    public const int Rejected = 0;

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var provided = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        var text = provided.FirstValue;
        if (provided == ValueProviderResult.None || string.IsNullOrWhiteSpace(text))
        {
            bindingContext.Result = ModelBindingResult.Success(null);
        }
        else
        {
            bindingContext.Result = ModelBindingResult.Success(
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : Rejected);
        }

        return Task.CompletedTask;
    }
}
