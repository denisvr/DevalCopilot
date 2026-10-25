using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>
/// The linked operation row and admission as stored: exactly one of the Agent attempt or verification execution identities is expected
/// for the step's kind. Agent status codes: 1 Running, 2 Completed, 3 Failed, 4 Interrupted, 0 unknown. Verification status codes: 1
/// Running, 2 Passed, 3 Failed, 4 TimedOut, 5 Cancelled, 6 Interrupted, 7 SourceChanged, 8 dispatch refused, 0 unknown. Process outcome
/// codes: 1 Exited, 2 TimedOut, 3 Cancelled, 4 Failed (a verification adapter fault), 0 absent, 99 unrecognized. Numbers are exact
/// integers or null. Agent outcome codes: 0 absent, 1 an outcome a failed dispatched attempt can carry with a timed-out or cancelled process
/// (LocalExecutionReleaseTerminal), 2 any other defined outcome, 99 unrecognized.
/// </summary>
internal sealed record LocalExecutionReleaseOperation(
    Guid? AgentId,
    Guid? AgentRunId,
    Guid? AgentWorkspaceId,
    Guid? AgentCheckpointId,
    int AgentStatusCode,
    int AgentContractCode,
    int AgentOutcomeCode,
    AgentOutcome? AgentOutcomeValue,
    string? AgentCheckpointFingerprint,
    DateTimeOffset? AgentDispatchedAtUtc,
    int AgentProcessOutcomeCode,
    int? AgentProcessExitCode,
    TimeSpan? AgentProcessDuration,
    Guid? ExecutionId,
    Guid? ExecutionProjectId,
    Guid? ExecutionWorkspaceId,
    Guid? ExecutionCheckpointId,
    string? ExecutionFingerprint,
    string? ExecutionCompletionFingerprint,
    Guid? ExecutionCommandId,
    int ExecutionStatusCode,
    int ExecutionOutcomeCode,
    int? ExecutionExitCode,
    DateTimeOffset? ExecutionDispatchedAtUtc,
    Guid? AdmissionId,
    Guid? AdmissionSequenceId,
    Guid? AdmissionStepId,
    int AdmissionStateCode,
    int? AdmissionOrdinal,
    int AdmissionKindCode,
    int? AdmissionPassNumber,
    int? AdmissionRecipeOrdinal,
    Guid? AdmissionSourceCheckpointId);
