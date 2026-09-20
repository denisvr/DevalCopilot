using System.Collections.Frozen;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed, Domain-owned policy for which <see cref="CollaborationMessageType"/> values a real
/// Agent attempt's own <see cref="AgentRole"/> may author. Keyed by role only — never by provider
/// or <see cref="ParticipantKind"/>, so a message's authorization can never depend on which
/// provider produced the attempt. Cardinality and union-shape validation (e.g. "exactly one
/// Acceptance or one to five Challenges") remain owned by each role-specific result handler;
/// reply-chain validity remains owned by <see cref="CollaborationMessageReplyPolicy"/>;
/// structured-content shape remains owned by <see cref="CollaborationMessageContentPolicy"/>. This
/// policy owns only whether a role may author a message type at all.
/// </summary>
public static class CollaborationMessageAuthorPolicy
{
    private static readonly FrozenDictionary<AgentRole, FrozenSet<CollaborationMessageType>> ByRole = BuildPolicy();

    /// <summary>Fails closed — throws rather than returning a default — for any
    /// <see cref="AgentRole"/> this policy does not recognize.</summary>
    public static IReadOnlySet<CollaborationMessageType> AllowedMessageTypes(AgentRole role) =>
        ByRole.TryGetValue(role, out var allowed)
            ? allowed
            : throw new ArgumentOutOfRangeException(nameof(role), role, "No collaboration-message author policy is defined for this role.");

    private static FrozenDictionary<AgentRole, FrozenSet<CollaborationMessageType>> BuildPolicy()
    {
        var policy = new Dictionary<AgentRole, FrozenSet<CollaborationMessageType>>
        {
            [AgentRole.Planner] = Frozen(
                CollaborationMessageType.Proposal, CollaborationMessageType.Question, CollaborationMessageType.Escalation),
            [AgentRole.CriticalReviewer] = Frozen(
                CollaborationMessageType.Acceptance, CollaborationMessageType.Challenge,
                CollaborationMessageType.Question, CollaborationMessageType.Escalation),
            [AgentRole.Resolver] = Frozen(
                CollaborationMessageType.Decision, CollaborationMessageType.Proposal,
                CollaborationMessageType.Question, CollaborationMessageType.Escalation),
            [AgentRole.Implementer] = Frozen(
                CollaborationMessageType.ExecutionReport, CollaborationMessageType.Question, CollaborationMessageType.Escalation),
            [AgentRole.CodeReviewer] = Frozen(
                CollaborationMessageType.ReviewApproval, CollaborationMessageType.ReviewFinding,
                CollaborationMessageType.Question, CollaborationMessageType.Escalation),
        };

        return policy.ToFrozenDictionary();
    }

    private static FrozenSet<CollaborationMessageType> Frozen(params CollaborationMessageType[] types) =>
        types.ToFrozenSet();
}
