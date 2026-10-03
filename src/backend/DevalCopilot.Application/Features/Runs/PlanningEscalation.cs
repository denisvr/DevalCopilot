using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Policies;
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

    /// <summary>The one canonical structured content this writer records for these durable identifiers (ADR-0020). It
    /// is never the historical form, which <see cref="PlanningEscalationLegacyForm"/> keeps recognizable.</summary>
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
                "Inspect the final proposal and the second-round decisions, then either separately authorize one implementation of that exact final plan and explicitly request it, or request a new plan with a new explicit planning request. Neither is chosen by this record.",
            consequences =
                "Authorizing permits exactly one implementation claim of that exact final plan. Only a durably committed implementation claim consumes the authorization: a refused request, or a claim that definitely does not commit, consumes nothing, and a committed claim stays consumed even if its execution later fails. A new plan replaces this lineage. This record selects nothing, grants nothing and approves nothing, and a third critical review or resolution is not available.",
            evidence,
            recommendedChoice =
                "Inspect the final proposal and every second-round decision before choosing.",
        });
    }

    /// <summary>Whether the stored content is, ordinally and as a whole, one of the two complete canonical forms (the
    /// current one or the historical one) recomputed from these validated identifiers. There is no mixed form, no
    /// semantic JSON equivalence, and no other wording or version.</summary>
    public static bool IsCanonicalContent(
        string structuredContentJson,
        Guid rootProposalId,
        Guid firstRevisionProposalId,
        Guid secondRevisionProposalId,
        IReadOnlyList<Guid> resolvedChallengeIds) =>
        string.Equals(
            structuredContentJson,
            BuildStructuredContentJson(rootProposalId, firstRevisionProposalId, secondRevisionProposalId, resolvedChallengeIds),
            StringComparison.Ordinal)
        || string.Equals(
            structuredContentJson,
            PlanningEscalationLegacyForm.BuildStructuredContentJson(
                rootProposalId, firstRevisionProposalId, secondRevisionProposalId, resolvedChallengeIds),
            StringComparison.Ordinal);

    public static CollaborationMessage Record(
        Guid runId,
        Guid rootProposalId,
        Guid firstRevisionProposalId,
        Guid secondRevisionProposalId,
        IReadOnlyList<Guid> resolvedChallengeIds,
        DateTimeOffset occurredAtUtc)
    {
        var structuredContentJson = BuildStructuredContentJson(
            rootProposalId, firstRevisionProposalId, secondRevisionProposalId, resolvedChallengeIds);

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
