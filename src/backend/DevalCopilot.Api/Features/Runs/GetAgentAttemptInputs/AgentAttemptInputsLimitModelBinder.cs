using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptInputs;

/// <summary>
/// Binds the optional <c>limit</c> of the recorded-input references (ADR-0053; the public parameter stays <c>int?</c>). An absent value or
/// one single blank value binds <see langword="null"/>, which keeps the ADR-0051 default of ten; a single value of ASCII decimal digits
/// without a leading zero that fits 32 bits binds as itself. Anything else (text, a sign, padding, a fraction, an exponent, non-ASCII
/// digits, an overflow or a repeated parameter, even a repeated blank one) binds <see cref="Rejected"/> and adds no model-state entry, so
/// the framework never writes its own body repeating the value: the query's validator refuses it with the shared Problem Details
/// contract, without the value. Nothing is clamped.
/// </summary>
public sealed class AgentAttemptInputsLimitModelBinder : IModelBinder
{
    /// <summary>Outside the accepted range of 1 to 64, so it is always refused.</summary>
    public const int Rejected = 0;

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var provided = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (provided == ValueProviderResult.None || (provided.Values.Count == 1 && string.IsNullOrWhiteSpace(provided.FirstValue)))
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
