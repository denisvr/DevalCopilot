namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>The scalar facts of one referenced verification execution, classified and guarded by the database without materializing a
/// stored enum or an unguarded number: <see cref="StatusCode"/> is 1 for Passed, 2 for Failed and 0 for anything else;
/// <see cref="OutcomeCode"/> is 0 for none, 1 for Exited, 2 for another known outcome and 99 for an unknown one; <see cref="Number"/> and
/// <see cref="ExitCode"/> are null unless the stored value is an exact integer in range, with <see cref="ExitCodePresent"/> telling a
/// rejected exit code from an absent one. Every string is read only within its byte guard and carries a flag. No path, executable,
/// argument or timeout is part of it.</summary>
internal sealed record VerificationExecutionRow(
    Guid Id,
    Guid ProjectId,
    Guid WorkspaceId,
    Guid CheckpointId,
    Guid CommandId,
    int? Number,
    string? CommandName,
    bool CommandNameOversized,
    string? ClaimFingerprint,
    bool ClaimFingerprintOversized,
    string? CompletionFingerprint,
    bool CompletionFingerprintOversized,
    int StatusCode,
    int OutcomeCode,
    int? ExitCode,
    bool ExitCodePresent,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc);
