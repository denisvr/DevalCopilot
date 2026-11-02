using DevalCopilot.Domain.Features.Processes;

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

    /// <summary>ADR-0072: the stdout completion fact of the dispatched process; <see langword="null"/> means not recorded.</summary>
    public OutputCaptureCompletion? StandardOutputCompletion { get; private set; }

    /// <summary>ADR-0072: the stderr completion fact of the dispatched process; <see langword="null"/> means not recorded.</summary>
    public OutputCaptureCompletion? StandardErrorCompletion { get; private set; }

    /// <summary>ADR-0072: the standard-input fact of the dispatched process; <see langword="null"/> means not recorded.</summary>
    public InputDeliveryCompletion? StandardInputDelivery { get; private set; }

    /// <summary>ADR-0072: the fixed environment profile version the host built for this execution; <see langword="null"/> means not
    /// recorded, as for every earlier execution.</summary>
    public string? EnvironmentProfileVersion { get; private set; }

    /// <summary>ADR-0072: the closed reason of a failure its exit code does not explain; <see langword="null"/> means none recorded.</summary>
    public VerificationExecutionDiagnostic? Diagnostic { get; private set; }

    /// <summary>ADR-0072: the recorded I/O facts, or <see langword="null"/> unless all three were recorded together.</summary>
    public ProcessIoCompletion? GetIoCompletion() =>
        StandardOutputCompletion is { } standardOutput && StandardErrorCompletion is { } standardError
        && StandardInputDelivery is { } standardInput
            ? new ProcessIoCompletion(standardOutput, standardError, standardInput)
            : null;

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

    /// <summary>The terminal pre-dispatch transition of a local-execution-sequence dispatch refusal (ADR-0060). A Running, never-dispatched
    /// execution ends without any process outcome, exit code or completion fingerprint. The Application recorder proves the linked step.</summary>
    public void MarkLocalExecutionSequenceDispatchRefused(DateTimeOffset nowUtc)
    {
        if (Status != VerificationExecutionStatus.Running || DispatchedAtUtc.HasValue)
        {
            throw new InvalidOperationException("Only an undispatched running verification execution can record a dispatch refusal.");
        }

        Status = VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused;
        CompletedAtUtc = nowUtc;
    }

    /// <remarks>ADR-0072: only an exit code zero with explicitly complete <paramref name="ioCompletion"/> passes; an exit code zero
    /// with incomplete or unknown I/O is recorded unchanged as Exited/0 and fails. <paramref name="environmentProfileVersion"/> and
    /// <paramref name="diagnostic"/> are recorded as given; a diagnostic is never recorded beside a pass.</remarks>
    public void Complete(
        VerificationExecutionOutcome outcome,
        int? exitCode,
        string completionFingerprintSha256,
        DateTimeOffset nowUtc,
        ProcessIoCompletion? ioCompletion,
        string? environmentProfileVersion = null,
        VerificationExecutionDiagnostic? diagnostic = null)
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

        if (diagnostic is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(diagnostic), diagnostic, "Not a defined verification diagnostic.");
        }

        if (environmentProfileVersion is { Length: 0 or > 128 })
        {
            throw new ArgumentException("An environment profile version must be non-empty and bounded.", nameof(environmentProfileVersion));
        }

        var ioComplete = ioCompletion is { IsComplete: true };
        Outcome = outcome;
        ExitCode = exitCode;
        CompletionFingerprintSha256 = completionFingerprintSha256;
        StandardOutputCompletion = ioCompletion?.StandardOutput;
        StandardErrorCompletion = ioCompletion?.StandardError;
        StandardInputDelivery = ioCompletion?.StandardInput;
        EnvironmentProfileVersion = environmentProfileVersion;
        Diagnostic = diagnostic;
        Status = !string.Equals(CheckpointFingerprintSha256, completionFingerprintSha256, StringComparison.Ordinal)
            ? VerificationExecutionStatus.SourceChanged
            : outcome switch
            {
                VerificationExecutionOutcome.Exited when exitCode == 0 && ioComplete && diagnostic is null => VerificationExecutionStatus.Passed,
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
