namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The exact reading of one persisted direct-guidance column. <see cref="IsMalformed"/> means the stored text is
/// not exactly its own normalized form (blank, overlong, unnormalized, containing a forbidden control character,
/// invalid Unicode, or unsafe content); it is never read as accepted guidance or as absence. <see cref="Text"/> is
/// non-null only for valid guidance.
/// </summary>
public readonly record struct DirectHumanGuidanceReading(bool IsMalformed, string? Text)
{
    public static DirectHumanGuidanceReading Absent => new(false, null);

    public static DirectHumanGuidanceReading Malformed => new(true, null);

    public bool IsAbsent => !IsMalformed && Text is null;
}
