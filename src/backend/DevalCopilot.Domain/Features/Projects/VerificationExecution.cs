namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// One durably claimed local verification execution. It snapshots the enabled recipe and the
/// exact source checkpoint before a child process can start, so later configuration edits or
/// source drift cannot be represented as evidence for this execution.
/// </summary>
public sealed class VerificationExecution
{
    private VerificationExecution()
    {
    }

    public static VerificationExecution Claim(
        Guid id,
        Guid projectId,
        int executionNumber,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        VerificationCommand command,
        DateTimeOffset claimedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(command);

        if (executionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(executionNumber));
        }

        if (workspace.ProjectId != projectId || command.ProjectId != projectId || checkpoint.WorkspaceId != workspace.Id)
        {
            throw new ArgumentException("The verification execution ownership does not match.");
        }

        return new VerificationExecution
        {
            Id = id,
            ProjectId = projectId,
            ExecutionNumber = executionNumber,
            GitWorkspaceId = workspace.Id,
            GitCheckpointId = checkpoint.Id,
            VerificationCommandId = command.Id,
            WorkspacePath = workspace.WorkspacePath,
            CheckpointFingerprintSha256 = checkpoint.FingerprintSha256,
            CommandName = command.Name,
            ExecutablePath = command.ExecutablePath,
            Arguments = command.Arguments.ToArray(),
            TimeoutSeconds = command.TimeoutSeconds,
            Status = VerificationExecutionStatus.Running,
            ClaimedAtUtc = claimedAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public int ExecutionNumber { get; private set; }

    public Guid GitWorkspaceId { get; private set; }

    public Guid GitCheckpointId { get; private set; }

    public Guid VerificationCommandId { get; private set; }

    public string WorkspacePath { get; private set; } = string.Empty;

    public string CheckpointFingerprintSha256 { get; private set; } = string.Empty;

    public string CommandName { get; private set; } = string.Empty;

    public string ExecutablePath { get; private set; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; private set; } = [];

    public int TimeoutSeconds { get; private set; }

    public VerificationExecutionStatus Status { get; private set; }

    public DateTimeOffset ClaimedAtUtc { get; private set; }

    public DateTimeOffset? DispatchedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public VerificationExecutionOutcome? Outcome { get; private set; }

    public int? ExitCode { get; private set; }

    public string? CompletionFingerprintSha256 { get; private set; }

    public void MarkDispatched(DateTimeOffset nowUtc)
    {
        if (Status != VerificationExecutionStatus.Running || DispatchedAtUtc.HasValue)
        {
            throw new InvalidOperationException("Only an undispatched running verification execution can be dispatched.");
        }

        DispatchedAtUtc = nowUtc;
    }

    public void MarkSourceChangedBeforeDispatch(string completionFingerprintSha256, DateTimeOffset nowUtc)
    {
        if (Status != VerificationExecutionStatus.Running || DispatchedAtUtc.HasValue)
        {
            throw new InvalidOperationException("Only an undispatched running verification execution can be invalidated.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(completionFingerprintSha256);
        CompletionFingerprintSha256 = completionFingerprintSha256;
        Status = VerificationExecutionStatus.SourceChanged;
        CompletedAtUtc = nowUtc;
    }

    public void Complete(
        VerificationExecutionOutcome outcome,
        int? exitCode,
        string completionFingerprintSha256,
        DateTimeOffset nowUtc)
    {
        if (Status != VerificationExecutionStatus.Running || !DispatchedAtUtc.HasValue)
        {
            throw new InvalidOperationException("Only a dispatched running verification execution can complete.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(completionFingerprintSha256);
        if (outcome == VerificationExecutionOutcome.Exited != exitCode.HasValue)
        {
            throw new ArgumentException("Only an exited process may have an exit code.", nameof(exitCode));
        }

        Outcome = outcome;
        ExitCode = exitCode;
        CompletionFingerprintSha256 = completionFingerprintSha256;
        Status = !string.Equals(CheckpointFingerprintSha256, completionFingerprintSha256, StringComparison.Ordinal)
            ? VerificationExecutionStatus.SourceChanged
            : outcome switch
            {
                VerificationExecutionOutcome.Exited when exitCode == 0 => VerificationExecutionStatus.Passed,
                VerificationExecutionOutcome.Exited => VerificationExecutionStatus.Failed,
                VerificationExecutionOutcome.TimedOut => VerificationExecutionStatus.TimedOut,
                VerificationExecutionOutcome.Cancelled => VerificationExecutionStatus.Cancelled,
                VerificationExecutionOutcome.Failed => VerificationExecutionStatus.Failed,
                _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
            };
        CompletedAtUtc = nowUtc;
    }

    public void Interrupt(DateTimeOffset nowUtc)
    {
        if (Status != VerificationExecutionStatus.Running)
        {
            throw new InvalidOperationException("Only a running verification execution can be interrupted.");
        }

        Status = VerificationExecutionStatus.Interrupted;
        CompletedAtUtc = nowUtc;
    }
}
