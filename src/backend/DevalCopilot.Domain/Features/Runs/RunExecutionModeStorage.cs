using System.Globalization;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The exact stored form of <see cref="RunExecutionMode"/>. The column is exposed to the Domain as one string through the
/// type-preserving mapping that also protects the Claude turn-limit columns (an INTEGER is its canonical digits; a REAL,
/// TEXT, BLOB, or other storage class carries a tag that is never the first character of a canonical integer). Only the
/// canonical digits of a defined mode are recognized, so a REAL that would truncate to a valid mode, an integer outside the
/// enum (including one that would overflow 32 bits), text, and BLOBs are all <see cref="Unrecognized"/> instead of being
/// coerced, thrown on, or rewritten.
/// </summary>
public static class RunExecutionModeStorage
{
    /// <summary>The value reported for any stored representation that is not exactly 0, 1, or 2. It is not a member of
    /// <see cref="RunExecutionMode"/> and is admitted by nothing.</summary>
    public const RunExecutionMode Unrecognized = (RunExecutionMode)(-1);

    public const string LegacyText = "0";

    public const string SimulatedText = "1";

    public const string ManualAgentText = "2";

    public static string Format(RunExecutionMode mode) => mode switch
    {
        RunExecutionMode.Legacy or RunExecutionMode.Simulated or RunExecutionMode.ManualAgent =>
            ((int)mode).ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Only a recognized mode has a stored form."),
    };

    public static RunExecutionMode Read(string? stored) => stored switch
    {
        LegacyText => RunExecutionMode.Legacy,
        SimulatedText => RunExecutionMode.Simulated,
        ManualAgentText => RunExecutionMode.ManualAgent,
        _ => Unrecognized,
    };

}
