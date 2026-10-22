namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// The scalar facts of the one verification execution a step links, classified and guarded by the database. <see cref="StatusCode"/> is 1
/// to 8 in the Domain order plus one (0 unknown); <see cref="OutcomeCode"/> is 0 for none, 1 to 4 in the Domain order plus one and 99
/// for an unknown stored value. Numbers are null unless exact integers in range, with <see cref="ExitCodePresent"/> separating a rejected
/// exit code from an absent one. The workspace path is never selected, and the executable and arguments only within their byte guards.
/// </summary>
internal sealed record LocalExecutionInspectionExecutionRow(
    Guid Id,
    Guid ProjectId,
    Guid WorkspaceId,
    Guid CheckpointId,
    Guid CommandId,
    int? Number,
    string? CommandName,
    bool CommandNameOversized,
    string? ExecutablePath,
    bool ExecutablePathOversized,
    byte[]? ArgumentsUtf8,
    bool ArgumentsOversized,
    int? TimeoutSeconds,
    string? ClaimFingerprint,
    bool ClaimFingerprintOversized,
    string? CompletionFingerprint,
    bool CompletionFingerprintOversized,
    bool CompletionFingerprintPresent,
    int StatusCode,
    int OutcomeCode,
    int? ExitCode,
    bool ExitCodePresent,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc);
