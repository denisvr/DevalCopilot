using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// An independent oracle of the two complete canonical escalation serializations. The wording is deliberately
/// duplicated from nothing in production: a change to the production writer or to the production legacy constants
/// can never silently change what these tests expect. Both forms share the identifier-derived evidence sentence.
/// </summary>
internal static class PlanningEscalationForms
{
    public static string Evidence(Guid root, Guid first, Guid second, IReadOnlyList<Guid> challenges) =>
        $"Root proposal {root}; first revision {first}; second revision {second}; {challenges.Count} second-round "
        + $"challenge(s) each decided once: {string.Join(", ", challenges)}.";

    public static string Legacy(Guid root, Guid first, Guid second, IReadOnlyList<Guid> challenges) =>
        JsonSerializer.Serialize(new
        {
            unresolvedDecision =
                "The second and final challenge-resolution round produced a revised proposal that has no further automated review or resolution.",
            options =
                "Decide manually whether the revised proposal is acceptable, or start a new explicit planning request. Neither is chosen by this record.",
            consequences =
                "The revised proposal is not implementable through this lineage and is not approved; a third review is not available.",
            evidence = Evidence(root, first, second, challenges),
            recommendedChoice =
                "Read the second-round decisions before starting any new planning request.",
        });

    public static string Current(Guid root, Guid first, Guid second, IReadOnlyList<Guid> challenges) =>
        JsonSerializer.Serialize(new
        {
            unresolvedDecision =
                "The second and final challenge-resolution round produced a revised proposal that has no further automated review or resolution.",
            options =
                "Inspect the final proposal and the second-round decisions, then either separately authorize one implementation of that exact final plan and explicitly request it, or request a new plan with a new explicit planning request. Neither is chosen by this record.",
            consequences =
                "Authorizing permits exactly one implementation claim of that exact final plan. Only a durably committed implementation claim consumes the authorization: a refused request, or a claim that definitely does not commit, consumes nothing, and a committed claim stays consumed even if its execution later fails. A new plan replaces this lineage. This record selects nothing, grants nothing and approves nothing, and a third critical review or resolution is not available.",
            evidence = Evidence(root, first, second, challenges),
            recommendedChoice =
                "Inspect the final proposal and every second-round decision before choosing.",
        });

    public static string Of(EscalationForm form, Guid root, Guid first, Guid second, IReadOnlyList<Guid> challenges) =>
        form switch
        {
            EscalationForm.Legacy => Legacy(root, first, second, challenges),
            EscalationForm.Current => Current(root, first, second, challenges),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

    /// <summary>A host-constructed, attemptless Orchestrator-to-Human escalation replying to the final Proposal, carrying
    /// exactly the given summary and content: the shape the production writer records, with a caller-chosen text.</summary>
    public static CollaborationMessage Record(
        Guid runId, Guid secondRevisionProposalId, string structuredContentJson, DateTimeOffset occurredAtUtc) =>
        CollaborationMessage.Record(
            Guid.NewGuid(),
            runId,
            attemptId: null,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForOrchestrator(),
            ParticipantIdentity.ForHuman(),
            CollaborationMessageType.Escalation,
            secondRevisionProposalId,
            "The second challenge-resolution round is complete and needs a human decision.",
            structuredContentJson,
            CollaborationMessageProvenance.HostConstructed,
            occurredAtUtc);
}
