using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationHistory;

/// <summary>
/// Binds the optional <c>maxBytes</c> of a verification output window (the public parameter stays <c>int?</c>). An absent key binds
/// <see langword="null"/>, which the endpoint reads as the 16 KiB default; a single canonical value of ASCII decimal digits without a
/// leading zero that fits 32 bits binds as itself. Anything else binds <see cref="Rejected"/> and adds no model-state entry: the query
/// validator refuses it with the shared Problem Details contract and never repeats the value. Nothing is clamped.
/// </summary>
public sealed class ProjectVerificationMaxBytesModelBinder : IModelBinder
{
    public const string QueryName = "maxBytes";

    /// <summary>Outside the accepted range of 64 to 65536, so it is always refused.</summary>
    public const int Rejected = 0;

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var provided = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (provided == ValueProviderResult.None)
        {
            bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        var text = provided.Values.Count == 1 ? provided.FirstValue : null;
        var canonical = text is { Length: > 0 and <= 10 } && text[0] is >= '1' and <= '9' && text.All(char.IsAsciiDigit);
        bindingContext.Result = ModelBindingResult.Success(
            canonical && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : Rejected);
        return Task.CompletedTask;
    }
}
