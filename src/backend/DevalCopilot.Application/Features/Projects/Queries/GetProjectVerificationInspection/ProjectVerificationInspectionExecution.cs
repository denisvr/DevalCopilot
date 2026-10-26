using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationInspection;

/// <summary>The recorded facts of one execution as read now. Status and outcome are recorded bookkeeping: neither a terminal nor a dispatched
/// record is a physical exit or a successful verification, and the command is the execution's immutable snapshot, never the current recipe.
/// <see cref="CompletionFingerprintSha256"/> may legitimately differ from the claim fingerprint for a SourceChanged execution.</summary>
public sealed record ProjectVerificationInspectionExecution(
    int ExecutionNumber,
    Guid CommandId,
    string CommandName,
    Guid WorkspaceId,
    Guid CheckpointId,
    int CheckpointNumber,
    string ClaimFingerprintSha256,
    string? CompletionFingerprintSha256,
    VerificationExecutionStatus Status,
    VerificationExecutionOutcome? Outcome,
    int? ExitCode,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    RecordedVerificationOutputDescriptor StandardOutput,
    RecordedVerificationOutputDescriptor StandardError);
