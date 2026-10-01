using System.Text;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one normalization and content policy that the optional human guidance of an authorization
/// (<see cref="ReviewCorrectionGuidance"/>) and the direct guidance of a mutation request
/// (<see cref="DirectHumanGuidance"/>) intentionally share: Unicode form C, line endings to <c>\n</c>,
/// surrounding whitespace trimmed, non-blank valid Unicode of at most <see cref="MaximumLength"/> UTF-16 code
/// units, no control character other than <c>\n</c>, and the shared bounded-summary content policy. Each owner
/// keeps its own reserved values and canonical representation.
/// </summary>
internal static class BoundedGuidanceText
{
    public const int MaximumLength = 600;

    /// <summary>Returns the normalized text, or <see langword="null"/> when the input is blank, too long, not valid
    /// Unicode, contains a forbidden control character, or fails the shared unsafe-text check. Never throws and never
    /// echoes the input.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Invalid UTF-16 (an unpaired surrogate) is rejected rather than silently replaced, so the
        // persisted text is always exactly what was accepted.
        for (var index = 0; index < raw.Length; index++)
        {
            if (char.IsHighSurrogate(raw[index]) && index + 1 < raw.Length && char.IsLowSurrogate(raw[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(raw[index]))
            {
                return null;
            }
        }

        string normalized;
        try
        {
            normalized = raw.Normalize(NormalizationForm.FormC)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Trim();
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (normalized.Length == 0 || normalized.Length > MaximumLength)
        {
            return null;
        }

        foreach (var character in normalized)
        {
            if (char.IsControl(character) && character != '\n')
            {
                return null;
            }
        }

        return CollaborationMessageContentPolicy.IsSafeSummary(normalized) ? normalized : null;
    }
}
