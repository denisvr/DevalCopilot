using System.Globalization;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunEventHistory;

/// <summary>The one canonical spelling of a sequence cursor: ASCII decimal digits only, no sign, padding, leading zero, fraction or
/// exponent, positive, and at most <see cref="long.MaxValue"/>. It is parsed exactly as text, never through floating point.</summary>
public static class RunEventHistoryCursor
{
    public static bool TryParse(string? text, out long sequence)
    {
        sequence = 0;
        if (string.IsNullOrEmpty(text) || text.Length > 19 || text[0] is < '1' or > '9' || !text.All(char.IsAsciiDigit))
        {
            return false;
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out sequence);
    }

    public static string Format(long sequence) => sequence.ToString(CultureInfo.InvariantCulture);
}
