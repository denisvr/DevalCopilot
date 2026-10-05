using System.Globalization;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one validity rule for an owner's optional run-scoped Codex account-usage warning (ADR-0026). <see langword="null"/> means no
/// warning is saved; otherwise the value is a whole used-percent from <see cref="Minimum"/> through <see cref="Maximum"/> inclusive,
/// and an explicit check that finds any observed window at or above it reports the warning as reached. The setting is advisory: it
/// never refuses, reserves or stops anything, is independent of the account-usage stop, and is never account access, provider
/// readiness or remaining quota.
///
/// <para>
/// The persisted column is exposed as one string whose form identifies the SQLite storage class, through the same type-preserving
/// mapping the other exact-integer settings use (see <see cref="Format"/> and <see cref="Read"/>): an INTEGER is its canonical
/// digits, a REAL is <c>r:</c> plus its exact IEEE-754 bits, a TEXT is <c>t:</c> plus the text verbatim, a BLOB is <c>b:</c> plus its
/// exact bytes in hex, and an absent value is null. A fractional, overflowing, textual, real or BLOB stored value is therefore
/// malformed, never truncated, coerced or read as a different number.
/// </para>
/// </summary>
public static class CodexAccountUsageWarning
{
    public const int Minimum = 1;

    public const int Maximum = 100;

    public static bool IsValid(int? percent) => percent is null or (>= Minimum and <= Maximum);

    /// <summary>The canonical stored text of a valid setting (invariant digits, no sign or leading zero), or <see langword="null"/>
    /// for no warning. A value outside the accepted range is never formatted.</summary>
    public static string? Format(int? percent)
    {
        if (!IsValid(percent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(percent), percent, $"A Codex account-usage warning must be between {Minimum} and {Maximum}.");
        }

        return percent?.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Reads stored text strictly: <see langword="null"/> is absent; exactly the canonical text of a whole number from
    /// <see cref="Minimum"/> through <see cref="Maximum"/> is valid; anything else is malformed.</summary>
    public static CodexAccountUsageWarningReading Read(string? stored)
    {
        if (stored is null)
        {
            return CodexAccountUsageWarningReading.Absent;
        }

        return stored.Length is >= 1 and <= 3
            && stored.All(char.IsAsciiDigit)
            && stored[0] != '0'
            && int.Parse(stored, NumberStyles.None, CultureInfo.InvariantCulture) is var value and >= Minimum and <= Maximum
                ? new CodexAccountUsageWarningReading(false, value)
                : CodexAccountUsageWarningReading.Malformed;
    }
}
