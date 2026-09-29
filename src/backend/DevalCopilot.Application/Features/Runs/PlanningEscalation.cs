using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// Builds the one orchestrator-authored human escalation a successful second challenge-resolution
/// round records atomically with its Decisions and revised Proposal. It is a fixed, bounded
/// statement plus durable identifiers and a count — never provider text, artifact content, paths,
/// or credentials — and it replies to the depth-two revised Proposal so the lineage stays one
/// reply chain. It records that a human decision is needed; it never approves, authorizes, or
/// selects anything.
/// </summary>
internal static class PlanningEscalation
{
    public const string Summary = "The second challenge-resolution round is complete and needs a human decision.";

    public static CollaborationMessage Record(
        Guid runId,
        Guid rootProposalId,
        Guid firstRevisionProposalId,
        Guid secondRevisionProposalId,
        IReadOnlyList<Guid> resolvedChallengeIds,
        DateTimeOffset occurredAtUtc)
    {
        var evidence =
            $"Root proposal {rootProposalId}; first revision {firstRevisionProposalId}; "
            + $"second revision {secondRevisionProposalId}; {resolvedChallengeIds.Count} second-round challenge(s) "
            + $"each decided once: {string.Join(", ", resolvedChallengeIds)}.";

        var structuredContentJson = JsonSerializer.Serialize(new
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

        return CollaborationMessage.Record(
            Guid.NewGuid(),
            runId,
            attemptId: null,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForOrchestrator(),
            ParticipantIdentity.ForHuman(),
            CollaborationMessageType.Escalation,
            secondRevisionProposalId,
            Summary,
            structuredContentJson,
            CollaborationMessageProvenance.HostConstructed,
            occurredAtUtc);
    }
}
