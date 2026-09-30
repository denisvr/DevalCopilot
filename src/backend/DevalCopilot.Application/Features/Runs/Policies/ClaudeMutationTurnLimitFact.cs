using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The bounded, read-side statement of a Claude agentic-turn-limit request: either an Agent attempt's
/// own immutable record (<see cref="ForAttempt"/>) or the Run's current saved request
/// (<see cref="ForRun(Run)"/>). Both describe a saved or snapshotted <em>request</em>: an attempt that has not
/// been dispatched, and a Run that has no attempt yet, still carry one. What a versioned adapter then does with a
/// recorded request (it passes the documented turn-limit argument) is a separate fact of that adapter contract and
/// is never inferred from this one. <see cref="MaxTurns"/> is present only for
/// <see cref="ClaudeMutationTurnLimitEvidence.Requested"/>. A legacy attempt with no request is
/// <see cref="ClaudeMutationTurnLimitEvidence.NotRecorded"/>, never an observed unlimited capacity, and
/// disagreeing or malformed stored facts are <see cref="ClaudeMutationTurnLimitEvidence.Unknown"/>.
/// It is provenance of a request, never a measured turn count.
/// </summary>
public sealed record ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence Evidence, int? MaxTurns)
{
    /// <summary>The fact for one Agent attempt, or <see langword="null"/> when the attempt is not on a Claude
    /// mutation response contract (initial implementation or review correction), where the concept does not apply.</summary>
    public static ClaudeMutationTurnLimitFact? ForAttempt(Attempt attempt)
    {
        if (attempt.AgentResponseContract is not (AgentResponseContract.ImplementationReport or AgentResponseContract.ReviewCorrection))
        {
            return null;
        }

        var evidence = attempt.GetMutationTurnLimitEvidence();
        return new ClaudeMutationTurnLimitFact(
            evidence, evidence == ClaudeMutationTurnLimitEvidence.Requested ? attempt.ReadAgentRequestedMaxTurns().Value : null);
    }

    /// <summary>The Run's current saved request: not requested, a valid request, or unknown when the stored value is
    /// malformed (never clamped).</summary>
    public static ClaudeMutationTurnLimitFact ForRun(Run run) => FromReading(run.ReadRequestedClaudeMaxTurns());

    /// <summary>The same fact from the stored text of the Run's column, for projections that do not load the Run.</summary>
    public static ClaudeMutationTurnLimitFact ForRunStored(string? stored) => FromReading(ClaudeMutationTurnLimit.Read(stored));

    /// <summary>The same fact from an already-parsed number: valid, absent, or unknown when out of range.</summary>
    public static ClaudeMutationTurnLimitFact ForRun(int? storedRequest) => storedRequest switch
    {
        null => FromReading(ClaudeMutationTurnLimitReading.Absent),
        { } value when ClaudeMutationTurnLimit.IsValid(value) => FromReading(new ClaudeMutationTurnLimitReading(false, value)),
        _ => FromReading(ClaudeMutationTurnLimitReading.Malformed),
    };

    private static ClaudeMutationTurnLimitFact FromReading(ClaudeMutationTurnLimitReading reading) => reading switch
    {
        { IsMalformed: true } => new ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence.Unknown, null),
        { Value: { } value } => new ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence.Requested, value),
        _ => new ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence.NotRequested, null),
    };
}
