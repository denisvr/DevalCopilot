using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryAttempt;

/// <summary>The bounded, untracked scalar projection of the attribution source. Nothing outside the listed columns is ever selected: no
/// message text. The attempt side uses the shared <see cref="ScalarAgentAttemptIdentity"/> projection.</summary>
internal static class CollaborationAttemptProjection
{
    private const int Absent = 0;
    private const int Unrecognized = 99;

    public static IQueryable<CollaborationAttemptSource> ProjectSource(this IQueryable<CollaborationMessage> messages)
    {
        const string version = CollaborationMessage.ProtocolVersionOne;

        return messages.Select(message => new CollaborationAttemptSource(
            message.Sequence,
            message.Id,
            message.ProtocolVersion == version,
            message.Type == CollaborationMessageType.Proposal
                || message.Type == CollaborationMessageType.Acceptance
                || message.Type == CollaborationMessageType.Challenge
                || message.Type == CollaborationMessageType.Question
                || message.Type == CollaborationMessageType.Decision
                || message.Type == CollaborationMessageType.ExecutionReport
                || message.Type == CollaborationMessageType.ReviewFinding
                || message.Type == CollaborationMessageType.RevisionResponse
                || message.Type == CollaborationMessageType.Escalation
                || message.Type == CollaborationMessageType.ReviewApproval
                || message.Type == CollaborationMessageType.HumanInstruction,
            message.Provenance == CollaborationMessageProvenance.Simulated ? 1
                : message.Provenance == CollaborationMessageProvenance.ProviderObserved ? 2
                : message.Provenance == CollaborationMessageProvenance.HostConstructed ? 3
                : message.Provenance == CollaborationMessageProvenance.HumanSubmitted ? 4
                : 0,
            message.ActorKind == ParticipantKind.None ? 1
                : message.ActorKind == ParticipantKind.Orchestrator ? 2
                : message.ActorKind == ParticipantKind.Agent ? 3
                : message.ActorKind == ParticipantKind.Human ? 4
                : 0,
            message.ActorAgentRole == null ? Absent
                : message.ActorAgentRole == AgentRole.Planner ? 1
                : message.ActorAgentRole == AgentRole.CriticalReviewer ? 2
                : message.ActorAgentRole == AgentRole.Resolver ? 3
                : message.ActorAgentRole == AgentRole.Implementer ? 4
                : message.ActorAgentRole == AgentRole.CodeReviewer ? 5
                : Unrecognized,
            message.ActorAgentProvider == null ? Absent
                : message.ActorAgentProvider == AgentProvider.Codex ? 1
                : message.ActorAgentProvider == AgentProvider.ClaudeCode ? 2
                : Unrecognized,
            message.AttemptId));
    }
}
