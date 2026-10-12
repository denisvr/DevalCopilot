using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryAttempt;

/// <summary>The two bounded, untracked scalar projections of the attempt-attribution read. Nothing outside the listed columns is ever
/// selected: no message text, no attempt prompt, path, session, artifact, checkpoint, outcome, token or guidance column.</summary>
internal static class CollaborationAttemptProjection
{
    private const int Absent = 0;
    private const int Unrecognized = 99;

    // A UTF-16 unit is at most three UTF-8 bytes, so a valid identifier of at most the Domain bound of 128 characters always fits; the
    // exact character bound is applied again in memory. length() of TEXT stops at an embedded NUL, so the guards bound the stored bytes
    // (CAST AS BLOB). A valid stored turn limit is at most three digits, so a stored value past a small bound is malformed storage.
    private const int IdentifierByteBound = AgentAssignmentSnapshotPolicy.MaximumIdentifierLength * 3;
    private const int MaxTurnsByteBound = 64;

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

    public static IQueryable<CollaborationAttemptRow> ProjectAttempt(this IQueryable<Attempt> attempts) =>
        attempts.Select(attempt => new CollaborationAttemptRow(
            attempt.Id,
            attempt.AttemptNumber,
            attempt.ClaimedAtUtc,
            attempt.CompletedAtUtc,
            attempt.AgentDispatchedAtUtc,
            attempt.Kind == AttemptKind.Agent,
            attempt.Status == AttemptStatus.Running ? 1
                : attempt.Status == AttemptStatus.Completed ? 2
                : attempt.Status == AttemptStatus.Failed ? 3
                : attempt.Status == AttemptStatus.Interrupted ? 4
                : Unrecognized,
            attempt.AgentRole == null ? Absent
                : attempt.AgentRole == AgentRole.Planner ? 1
                : attempt.AgentRole == AgentRole.CriticalReviewer ? 2
                : attempt.AgentRole == AgentRole.Resolver ? 3
                : attempt.AgentRole == AgentRole.Implementer ? 4
                : attempt.AgentRole == AgentRole.CodeReviewer ? 5
                : Unrecognized,
            attempt.AgentProvider == null ? Absent
                : attempt.AgentProvider == AgentProvider.Codex ? 1
                : attempt.AgentProvider == AgentProvider.ClaudeCode ? 2
                : Unrecognized,
            attempt.AgentResponseContract == null ? Absent
                : attempt.AgentResponseContract == AgentResponseContract.Proposal ? 1
                : attempt.AgentResponseContract == AgentResponseContract.CriticalReview ? 2
                : attempt.AgentResponseContract == AgentResponseContract.ChallengeResolution ? 3
                : attempt.AgentResponseContract == AgentResponseContract.ImplementationReport ? 4
                : attempt.AgentResponseContract == AgentResponseContract.ImplementationReview ? 5
                : attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection ? 6
                : attempt.AgentResponseContract == AgentResponseContract.VerificationDiagnosis ? 7
                : Unrecognized,
            attempt.AgentPermissionProfile == null ? Absent
                : attempt.AgentPermissionProfile == AgentPermissionProfile.Unknown ? 1
                : attempt.AgentPermissionProfile == AgentPermissionProfile.WorkspaceEditOnly ? 2
                : attempt.AgentPermissionProfile == AgentPermissionProfile.ReadOnly ? 3
                : Unrecognized,
            ((byte[])(object)attempt.AgentRequestedModel!).Length <= IdentifierByteBound ? attempt.AgentRequestedModel : null,
            attempt.AgentRequestedModel != null && ((byte[])(object)attempt.AgentRequestedModel).Length > IdentifierByteBound,
            ((byte[])(object)attempt.AgentObservedModel!).Length <= IdentifierByteBound ? attempt.AgentObservedModel : null,
            attempt.AgentObservedModel != null && ((byte[])(object)attempt.AgentObservedModel).Length > IdentifierByteBound,
            ((byte[])(object)attempt.AgentRequestedEffort!).Length <= IdentifierByteBound ? attempt.AgentRequestedEffort : null,
            attempt.AgentRequestedEffort != null && ((byte[])(object)attempt.AgentRequestedEffort).Length > IdentifierByteBound,
            ((byte[])(object)attempt.AgentObservedEffort!).Length <= IdentifierByteBound ? attempt.AgentObservedEffort : null,
            attempt.AgentObservedEffort != null && ((byte[])(object)attempt.AgentObservedEffort).Length > IdentifierByteBound,
            ((byte[])(object)attempt.AgentAdapterContractVersion!).Length <= IdentifierByteBound ? attempt.AgentAdapterContractVersion : null,
            attempt.AgentAdapterContractVersion != null && ((byte[])(object)attempt.AgentAdapterContractVersion).Length > IdentifierByteBound,
            ((byte[])(object)EF.Property<string?>(attempt, Attempt.AgentRequestedMaxTurnsStorageProperty)!).Length <= MaxTurnsByteBound
                ? EF.Property<string?>(attempt, Attempt.AgentRequestedMaxTurnsStorageProperty)
                : null,
            EF.Property<string?>(attempt, Attempt.AgentRequestedMaxTurnsStorageProperty) != null
                && ((byte[])(object)EF.Property<string?>(attempt, Attempt.AgentRequestedMaxTurnsStorageProperty)!).Length > MaxTurnsByteBound));
}
