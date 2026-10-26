using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary>The proved facts of one selected execution as read now (ADR-0069): its project-scoped number, snapshot command identity and
/// name, the workspace and checkpoint it recorded with that checkpoint's positive number, the claim and optional completion fingerprints,
/// its recorded status and process outcome, its optional exit code and its claim, dispatch and completion times. Recorded bookkeeping
/// only: neither a terminal nor a dispatched record is a physical exit or a successful verification.</summary>
internal sealed record ProjectVerificationRecordedExecution(
    int Number,
    Guid CommandId,
    string CommandName,
    Guid WorkspaceId,
    Guid CheckpointId,
    int CheckpointNumber,
    string ClaimFingerprint,
    string? CompletionFingerprint,
    VerificationExecutionStatus Status,
    VerificationExecutionOutcome? Outcome,
    int? ExitCode,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc);
