using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The bounded, read-side statement of the direct human guidance recorded on one mutation attempt's own immutable
/// snapshot, nothing more. <see cref="Text"/> is present only for <see cref="DirectHumanGuidanceEvidence.Provided"/>,
/// a coherent v2 mutation attempt whose snapshot is exactly accepted text. A coherent attempt with a null snapshot is
/// <see cref="DirectHumanGuidanceEvidence.NotRecorded"/>: no direct guidance was recorded, which is neither proof
/// that no text was submitted nor a claim about when the attempt was claimed. Disagreeing or malformed stored facts
/// are <see cref="DirectHumanGuidanceEvidence.Unknown"/> with no text. It is distinct from the extra-correction
/// authorization and proves what the host supplied, never that a provider followed it.
/// </summary>
public sealed record DirectHumanGuidanceFact(DirectHumanGuidanceEvidence Evidence, string? Text)
{
    /// <summary>The fact for one Agent attempt, or <see langword="null"/> when the attempt is not on a Claude mutation
    /// response contract (initial implementation or review correction), where the concept does not apply.</summary>
    public static DirectHumanGuidanceFact? ForAttempt(Attempt attempt)
    {
        if (attempt.AgentResponseContract is not (AgentResponseContract.ImplementationReport or AgentResponseContract.ReviewCorrection))
        {
            return null;
        }

        var evidence = attempt.GetDirectHumanGuidanceEvidence();
        return new DirectHumanGuidanceFact(
            evidence, evidence == DirectHumanGuidanceEvidence.Provided ? attempt.ReadAgentDirectHumanGuidance().Text : null);
    }
}
