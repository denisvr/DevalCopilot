using System.Text.Json;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The one historical canonical serialization of the second-round escalation content, written before ADR-0020 and kept so
/// that an escalation already recorded in this exact form, with its grants and downstream chains, stays valid. It is never
/// written again: <see cref="PlanningEscalation"/> records only its current form. The constants and the evidence sentence
/// are deliberately duplicated rather than shared with the writer, so a later change to the writer can never alter what a
/// historical record is compared against.
/// </summary>
internal static class PlanningEscalationLegacyForm
{
    public static string BuildStructuredContentJson(
        Guid rootProposalId,
        Guid firstRevisionProposalId,
        Guid secondRevisionProposalId,
        IReadOnlyList<Guid> resolvedChallengeIds)
    {
        var evidence =
            $"Root proposal {rootProposalId}; first revision {firstRevisionProposalId}; "
            + $"second revision {secondRevisionProposalId}; {resolvedChallengeIds.Count} second-round challenge(s) "
            + $"each decided once: {string.Join(", ", resolvedChallengeIds)}.";

        return JsonSerializer.Serialize(new
        {
            unresolvedDecision =
                "The second and final challenge-resolution round produced a revised proposal that has no further automated review or resolution.",
            options =
                "Decide manually whether the revised proposal is acceptable, or start a new explicit planning request. Neither is chosen by this record.",
            consequences =
                "The revised proposal is not implementable through this lineage and is not approved; a third review is not available.",
            evidence,
            recommendedChoice =
                "Read the second-round decisions before starting any new planning request.",
        });
    }
}
