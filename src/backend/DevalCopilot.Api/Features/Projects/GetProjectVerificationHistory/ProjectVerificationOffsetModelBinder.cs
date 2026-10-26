using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationHistory;

/// <summary>
/// Binds the optional <c>fromOffset</c> of a verification output window (the public parameter stays <c>long?</c>). An absent key binds
/// <see langword="null"/>, which the endpoint reads as 0; a single canonical value of ASCII decimal digits without a leading zero that
/// fits 64 bits binds as itself. Anything else (blank, text, a sign, padding, a fraction, an exponent, non-ASCII digits, an overflow or
/// a repeated parameter) binds <see cref="Rejected"/> and adds no model-state entry: the query validator refuses it and the shared
/// Problem Details contract answers, without the value. A value above 2^53 - 1 is refused by the same validator.
/// </summary>
public sealed class ProjectVerificationOffsetModelBinder : IModelBinder
{
    public const string QueryName = "fromOffset";

    /// <summary>Outside the accepted range of 0 to 9007199254740991, so it is always refused.</summary>
    public const long Rejected = -1;

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
        var canonical = text is { Length: > 0 and <= 19 }
            && text.All(char.IsAsciiDigit)
            && (text.Length == 1 || text[0] != '0');
        bindingContext.Result = ModelBindingResult.Success(
            canonical && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : Rejected);
        return Task.CompletedTask;
    }
}
