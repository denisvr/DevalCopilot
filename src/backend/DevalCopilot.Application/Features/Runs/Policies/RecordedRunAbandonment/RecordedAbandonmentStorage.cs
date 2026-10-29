using System.Globalization;
using System.Text;

namespace DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;

/// <summary>
/// Admits the raw stored bytes of an identifier, a time and a text of the recorded abandonment inspection (ADR-0071), which the
/// projection bounds in the database without converting them, so a corrupt value is an explicit refusal and never a conversion fault.
/// The only supported stored identifier is the exact canonical 36-character uppercase D-format text of a nonempty identifier (the form
/// the provider writes and compares). A supported time is the provider writer's text with any offset the writer produced (so a nondefault
/// offset and the default instant stay supported). A text is well-formed UTF-8 only; nothing is trimmed, normalized or repaired, and an
/// oversized, malformed or non-text value is none of them.
/// </summary>
internal static class RecordedAbandonmentStorage
{
    /// <summary>The stored identifier is the 36-character D-format text; it is ASCII, so its byte bound is its character bound.</summary>
    public const int IdentifierBytes = 36;

    /// <summary>Every stored time representation is bounded to 64 bytes; the provider writer's text is at most 33.</summary>
    public const int TimeBytes = 64;

    /// <summary>The recorded reason is at most 2048 bytes of UTF-8 (ADR-0031).</summary>
    public const int ReasonBytes = 2048;

    /// <summary>The event payload is at most 16384 bytes: a reason of 2048 UTF-16 units, each escaped as six ASCII bytes, and the
    /// envelope fit.</summary>
    public const int PayloadBytes = 16384;

    private static readonly string[] StoredTimeFormats = ["yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd HH:mm:sszzz"];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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

    /// <summary>The provider writer's time at full tick precision and any representable offset.</summary>
    public static DateTimeOffset? ParseTime(byte[]? stored)
    {
        if (stored is not { Length: > 0 and <= TimeBytes } || !IsPrintableAscii(stored))
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(
                Encoding.ASCII.GetString(stored), StoredTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;
    }

    /// <summary>The exact text of well-formed UTF-8 bytes, or <see langword="null"/> when they are malformed.</summary>
    public static string? ParseText(byte[]? stored)
    {
        if (stored is null)
        {
            return null;
        }

        try
        {
            return StrictUtf8.GetString(stored);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool IsPrintableAscii(byte[] stored) => stored.All(value => value is >= 0x20 and <= 0x7E);
}
