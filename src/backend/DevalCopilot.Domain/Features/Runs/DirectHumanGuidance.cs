namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The optional, bounded, advisory guidance a human may submit together with an explicit initial implementation
/// or ordinary review-correction request. The accepted text is stored once, immutably, on the exact claimed
/// Attempt and reaches only that attempt's sealed context. It clarifies work the authoritative plan or findings
/// already authorize; it grants no authority, attempt, budget, or permission, and a stored value proves what the
/// host supplied, not that a provider followed it. The lexical safety check it shares with
/// <see cref="BoundedGuidanceText"/> is a best-effort filter only.
/// </summary>
public static class DirectHumanGuidance
{
    public const int MaximumLength = BoundedGuidanceText.MaximumLength;

    /// <summary>Deterministically normalizes (see <see cref="BoundedGuidanceText"/>). Returns
    /// <see langword="null"/> for blank, overlong, invalid, or unsafe text. Never throws and never echoes the input.</summary>
    public static string? Normalize(string? raw) => BoundedGuidanceText.Normalize(raw);

    /// <summary>Reads one persisted value: absent for null, valid only when the stored text is exactly its own
    /// normalized form, malformed otherwise.</summary>
    public static DirectHumanGuidanceReading Read(string? stored)
    {
        if (stored is null)
        {
            return DirectHumanGuidanceReading.Absent;
        }

        return string.Equals(Normalize(stored), stored, StringComparison.Ordinal)
            ? new DirectHumanGuidanceReading(false, stored)
            : DirectHumanGuidanceReading.Malformed;
    }
}
