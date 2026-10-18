namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>One proved member of the recorded verification set, in recorded order: the membership's identities, the referenced
/// execution's positive number and snapshot command name, whether it is the admitted Failed/Exited/nonzero shape (otherwise
/// Passed/Exited/0), its exit code and its claim, dispatch and completion times as read now.</summary>
internal sealed record RecordedVerificationMember(
    int Sequence,
    Guid CommandId,
    Guid ExecutionId,
    int ExecutionNumber,
    string CommandName,
    bool Failed,
    int ExitCode,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset DispatchedAtUtc,
    DateTimeOffset CompletedAtUtc);
