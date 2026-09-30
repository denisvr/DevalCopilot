using System.Globalization;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one validity rule for an owner's requested Claude agentic-turn limit for the two mutating Claude paths
/// (initial implementation and review correction), shared by the Run, the Attempt snapshot, and the adapters'
/// fail-closed argument check so they cannot drift. <see langword="null"/> means no DevalCopilot override;
/// otherwise the value is a whole number from <see cref="Minimum"/> through <see cref="Maximum"/> inclusive.
/// Validity is a request-syntax rule, never a measured turn count, a token, cost, or account ceiling, or proof
/// that the provider honors the request.
///
/// <para>
/// The persisted column is exposed as one string whose form identifies the SQLite storage class, through a
/// type-preserving mapping (see <see cref="Format"/> and <see cref="Read"/>): an INTEGER is its canonical digits (the
/// only form accepted as a request), a REAL is <c>r:</c> plus its exact IEEE-754 bits, a TEXT is <c>t:</c> plus the text
/// verbatim, a BLOB is <c>b:</c> plus its exact bytes in hex, and an absent value is null. A fractional, overflowing,
/// textual, real, or BLOB stored value (even one holding digits) is therefore seen as malformed instead of being
/// truncated, overflowing during materialization, decoded, or read as a different number.
/// </para>
/// </summary>
public static class ClaudeMutationTurnLimit
{
    public const int Minimum = 1;

    public const int Maximum = 100;

    public static bool IsValid(int? maxTurns) => maxTurns is null or (>= Minimum and <= Maximum);

    /// <summary>The canonical stored text of a valid request (invariant digits, no sign or leading zero), or
    /// <see langword="null"/> for no request. A value outside the accepted range is never formatted.</summary>
    public static string? Format(int? maxTurns)
    {
        if (!IsValid(maxTurns))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTurns), maxTurns, $"A requested Claude turn limit must be between {Minimum} and {Maximum}.");
        }

        return maxTurns?.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Reads stored text strictly: <see langword="null"/> is absent; exactly the canonical text of a whole
    /// number from <see cref="Minimum"/> through <see cref="Maximum"/> is valid; anything else is malformed.</summary>
    public static ClaudeMutationTurnLimitReading Read(string? stored)
    {
        if (stored is null)
        {
            return ClaudeMutationTurnLimitReading.Absent;
        }

        return stored.Length is >= 1 and <= 3
            && stored.All(char.IsAsciiDigit)
            && stored[0] != '0'
            && int.Parse(stored, NumberStyles.None, CultureInfo.InvariantCulture) is var value and >= Minimum and <= Maximum
                ? new ClaudeMutationTurnLimitReading(false, value)
                : ClaudeMutationTurnLimitReading.Malformed;
    }
}
