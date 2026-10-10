namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// One read-only observation of a stage, combined from the public status and message-evidence routes and from the disposable
/// rows and sealed artifacts: only closed names, numbers, flags and opaque identities. The reading never holds a path, a prompt, a
/// provider output or a manifest; the checks that need those bytes are made where the bytes are and arrive here as flags.
/// </summary>
public sealed record StageReading
{
    public int AttemptsInRun { get; init; }

    public int DispatchedAttemptsInRun { get; init; }

    public bool AttemptFound { get; init; }

    public Guid AttemptId { get; init; }

    public int AttemptNumber { get; init; }

    public string Provider { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public string Contract { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? Outcome { get; init; }

    public bool Dispatched { get; init; }

    public string? ProcessOutcome { get; init; }

    public int? ExitCode { get; init; }

    public long? DurationMilliseconds { get; init; }

    /// <summary>The public status route and the stored attempt agree on status and outcome.</summary>
    public bool StatusRouteAgrees { get; init; }

    public Guid? ReviewedProposalMessageId { get; init; }

    public int MessageCount { get; init; }

    public Guid? MessageId { get; init; }

    public string? MessageType { get; init; }

    public Guid? InReplyToMessageId { get; init; }

    public Guid? MessageAttemptId { get; init; }

    public string? MessageActor { get; init; }

    public string? MessageProvenance { get; init; }

    /// <summary>The public message-evidence route agrees with the stored attempt on provider, role, outcome and recorded inputs.</summary>
    public bool EvidenceRouteAgrees { get; init; }

    public IReadOnlyList<Guid> InputMessageIds { get; init; } = [];

    public bool ManifestPresent { get; init; }

    public bool ManifestBytesAgree { get; init; }

    public bool ManifestContractAgrees { get; init; }

    public bool ManifestObjectiveAgrees { get; init; }

    public Guid? ManifestProposalMessageId { get; init; }

    public int ArtifactCount { get; init; }

    public int ArtifactsAgreeing { get; init; }

    public bool IsTerminal => AttemptFound && Status != "Running";

    /// <summary>Host-measured process evidence: an exit with its code, or a measured timeout. A dispatch marker, a cancellation
    /// outcome or a missing result is not evidence that a process ran, and none of them proves that none did.</summary>
    public bool ExecutionObserved =>
        ProcessOutcome == "TimedOut" || (ProcessOutcome == "Exited" && ExitCode is not null);
}
