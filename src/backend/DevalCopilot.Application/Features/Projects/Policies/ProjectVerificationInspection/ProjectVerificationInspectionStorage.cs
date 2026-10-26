using System.Globalization;
using System.Text;

namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary>
/// Admits the raw stored bytes of an identifier, a time or a fingerprint (ADR-0069), which the projections bound in the database without
/// converting them, so a corrupt value is an explicit refusal and never a conversion fault. The only supported stored identifier is the
/// exact canonical 36-character uppercase D-format text of a nonempty identifier (the form the provider writes and compares), and the only
/// supported stored time is the provider writer's text with the required zero offset. Nothing is trimmed, braced, unhyphenated,
/// lowercased or otherwise normalized, and an oversized, NUL-containing, non-ASCII or non-text value is none of them.
/// </summary>
internal static class ProjectVerificationInspectionStorage
{
    /// <summary>The stored identifier is the 36-character D-format text; it is ASCII, so its byte bound is its character bound.</summary>
    public const int IdentifierBytes = 36;

    /// <summary>The stored time is the provider writer's text (yyyy-MM-dd HH:mm:ss.FFFFFFFzzz, at most 33 characters).</summary>
    public const int TimeBytes = 33;

    public const int FingerprintLength = 64;

    private const string StoredTimeFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz";

    public static Guid? ParseIdentifier(byte[]? stored)
    {
        if (stored is not { Length: IdentifierBytes } || !IsPrintableAscii(stored))
        {
            return null;
        }

        var text = Encoding.ASCII.GetString(stored);
        return Guid.TryParseExact(text, "D", out var identifier)
            && identifier != Guid.Empty
            && string.Equals(text, identifier.ToString("D").ToUpperInvariant(), StringComparison.Ordinal)
            ? identifier
            : null;
    }

    /// <summary>A nondefault UTC time at the writer's tick precision.</summary>
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

    /// <summary>Exactly 64 lowercase hexadecimal characters.</summary>
    public static bool IsFingerprint(string? value) =>
        value is { Length: FingerprintLength } && value.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static bool IsPrintableAscii(byte[] stored) => stored.All(value => value is >= 0x20 and <= 0x7E);
}
