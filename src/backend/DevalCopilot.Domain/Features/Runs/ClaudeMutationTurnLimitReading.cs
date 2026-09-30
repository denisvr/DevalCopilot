namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The exact reading of one persisted turn-limit column. <see cref="IsMalformed"/> means the stored
/// representation is not a canonical whole number inside the accepted range (a fraction, a number too large for
/// the range, text, a sign or leading zero, or any other non-integer storage value); it is never read as a valid
/// request, never as null or zero, and never clamped. <see cref="Value"/> is non-null only for a valid request.
/// </summary>
public readonly record struct ClaudeMutationTurnLimitReading(bool IsMalformed, int? Value)
{
    public static ClaudeMutationTurnLimitReading Absent => new(false, null);

    public static ClaudeMutationTurnLimitReading Malformed => new(true, null);

    public bool IsAbsent => !IsMalformed && Value is null;
}
