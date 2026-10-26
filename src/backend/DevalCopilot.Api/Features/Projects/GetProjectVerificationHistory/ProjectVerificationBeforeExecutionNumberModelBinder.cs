using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationHistory;

/// <summary>
/// Binds the optional <c>beforeExecutionNumber</c> of the verification history page (the public parameter stays <c>int?</c>). An absent
/// key binds <see langword="null"/>; a single canonical value of ASCII decimal digits without a leading zero that fits 32 bits binds as
/// itself. Anything else (blank, text, a sign, padding, a fraction, an exponent, non-ASCII digits, an overflow or a repeated parameter)
/// binds <see cref="Rejected"/> and adds no model-state entry, so the framework never writes its own body repeating the value: the query
/// validator refuses it and the shared Problem Details contract answers, without the value.
/// </summary>
public sealed class ProjectVerificationBeforeExecutionNumberModelBinder : IModelBinder
{
    public const string QueryName = "beforeExecutionNumber";

    /// <summary>Outside the accepted range of 1 to 2147483647, so it is always refused.</summary>
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
