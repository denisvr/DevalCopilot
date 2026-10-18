using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationOutputWindow;

/// <summary>
/// Binds the optional <c>maxBytes</c> of the verification output window (the public parameter stays <c>int?</c>). An absent or blank
/// value binds <see langword="null"/>, which the endpoint reads as the 16 KiB default; a single canonical value of ASCII decimal digits
/// without a leading zero that fits 32 bits binds as itself. Anything else binds <see cref="Rejected"/> and adds no model-state entry:
/// the query validator refuses it with the shared Problem Details contract and never repeats the value. Nothing is clamped.
/// </summary>
public sealed class VerificationOutputWindowMaxBytesModelBinder : IModelBinder
{
    /// <summary>Outside the accepted range of 64 to 65536, so it is always refused.</summary>
    public const int Rejected = 0;

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var provided = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (provided == ValueProviderResult.None || provided.Values.All(string.IsNullOrWhiteSpace))
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
