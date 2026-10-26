using System.Globalization;
using System.Text;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>
/// Admits the raw stored bytes of an identifier or a time (ADR-0068), which the projection bounds in the database without converting
/// them, so a corrupt value is an explicit refusal and never a conversion fault. The only supported stored identifier is the exact
/// 36-character D-format text of a nonempty identifier, and the only supported stored time is the provider writer's text with the
/// required zero offset. Nothing is trimmed, braced, unhyphenated or otherwise normalized, and a withheld (oversized),
/// NUL-containing, non-ASCII or non-text value is neither.
/// </summary>
internal static class PlanningSequenceInspectionStorage
{
    /// <summary>The stored identifier is the 36-character D-format text; it is ASCII, so its byte bound is its character bound.</summary>
    public const int IdentifierBytes = 36;

    /// <summary>The stored time is the provider writer's text (yyyy-MM-dd HH:mm:ss.FFFFFFFzzz, at most 33 characters).</summary>
    public const int TimeBytes = 33;

    private const string StoredTimeFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz";

    public static Guid? ParseIdentifier(byte[]? stored)
    {
        if (stored is not { Length: IdentifierBytes } || !IsPrintableAscii(stored))
        {
            return null;
        }

        return Guid.TryParseExact(Encoding.ASCII.GetString(stored), "D", out var identifier) && identifier != Guid.Empty
            ? identifier
            : null;
    }

    public static DateTimeOffset? ParseUtc(byte[]? stored)
    {
        if (stored is not { Length: > 0 and <= TimeBytes } || !IsPrintableAscii(stored))
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(
                Encoding.ASCII.GetString(stored), StoredTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            && time != default
            && time.Offset == TimeSpan.Zero
            ? time
            : null;
    }

    private static bool IsPrintableAscii(byte[] stored) => stored.All(value => value is >= 0x20 and <= 0x7E);
}
