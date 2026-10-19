using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptOutputs;

/// <summary>The bounded, untracked scalar projection of the output-set read. Nothing outside the listed columns is ever selected: no
/// message summary or content JSON, no reply pointer and no attempt data beyond the shared identity projection.</summary>
internal static class AgentAttemptOutputsProjection
{
    public const int ProviderObservedCode = 2;
    public const int AgentKindCode = 3;

    private const int Absent = 0;
    private const int Unrecognized = 99;

    public static IQueryable<AgentAttemptOutputRow> ProjectOutput(this IQueryable<CollaborationMessage> messages)
    {
        const string version = CollaborationMessage.ProtocolVersionOne;

        return messages.Select(message => new AgentAttemptOutputRow(
            message.Sequence,
            message.Id,
            message.RunId,
            message.OccurredAtUtc,
            message.ProtocolVersion == version,
            message.Type == CollaborationMessageType.Proposal ? 1
                : message.Type == CollaborationMessageType.Acceptance ? 2
                : message.Type == CollaborationMessageType.Challenge ? 3
                : message.Type == CollaborationMessageType.Question ? 4
                : message.Type == CollaborationMessageType.Decision ? 5
                : message.Type == CollaborationMessageType.ExecutionReport ? 6
                : message.Type == CollaborationMessageType.ReviewFinding ? 7
                : message.Type == CollaborationMessageType.RevisionResponse ? 8
                : message.Type == CollaborationMessageType.Escalation ? 9
                : message.Type == CollaborationMessageType.ReviewApproval ? 10
                : message.Type == CollaborationMessageType.HumanInstruction ? 11
                : 0,
            message.Provenance == CollaborationMessageProvenance.Simulated ? 1
                : message.Provenance == CollaborationMessageProvenance.ProviderObserved ? ProviderObservedCode
                : message.Provenance == CollaborationMessageProvenance.HostConstructed ? 3
                : message.Provenance == CollaborationMessageProvenance.HumanSubmitted ? 4
                : 0,
            message.ActorKind == ParticipantKind.None ? 1
                : message.ActorKind == ParticipantKind.Orchestrator ? 2
                : message.ActorKind == ParticipantKind.Agent ? AgentKindCode
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
                : Unrecognized));
    }
}
