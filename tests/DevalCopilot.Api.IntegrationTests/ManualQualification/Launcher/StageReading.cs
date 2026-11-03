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

    /// <summary>The adapter contract version recorded on the stored attempt (ADR-0072), or empty when none was recorded. The public
    /// attempt-evidence route never carries it, so it comes from the disposable row only.</summary>
    public string AdapterContractVersion { get; init; } = string.Empty;

    /// <summary>The recorded I/O completion facts of the provider process, closed enum names, or null when not recorded together.</summary>
    public string? StandardOutputCompletion { get; init; }

    public string? StandardErrorCompletion { get; init; }

    public string? StandardInputDelivery { get; init; }

    /// <summary>Required I/O completion: both output streams complete and standard input delivered (or not redirected), all recorded.
    /// Derived, so it can never disagree with the three recorded facts; false also covers unrecorded.</summary>
    public bool RequiredIoComplete =>
        StandardOutputCompletion == "Complete"
        && StandardErrorCompletion == "Complete"
        && StandardInputDelivery is "Complete" or "NotRedirected";

    /// <summary>The closed invocation-diagnostic state of the stored attempt: NotRecorded, Recorded or Unknown.</summary>
    public string InvocationDiagnosticState { get; init; } = "NotRecorded";

    /// <summary>The recorded closed category, only when the state is Recorded. A category is what was observed or reported, never a
    /// verified cause.</summary>
    public string? InvocationDiagnosticCategory { get; init; }

    public string? InvocationDiagnosticVersion { get; init; }

    /// <summary>Whether the public attempt-evidence route was compared with the stored attempt on status, outcome, process, I/O
    /// completion and diagnostic, and with what result. Independent of any message, so a failed attempt with no message still has it.</summary>
    public EvidenceRouteComparisonStatus AttemptEvidenceRouteComparison { get; init; }

    public bool AttemptEvidenceRouteAgrees => AttemptEvidenceRouteComparison == EvidenceRouteComparisonStatus.Agrees;

    /// <summary>The number of produced messages the timeline actually listed for the attempt, or null when it was not enumerated
    /// (no attempt, or a still running one). An unobserved count is never zero.</summary>
    public int? MessageCount { get; init; }

    public Guid? MessageId { get; init; }

    public string? MessageType { get; init; }

    public Guid? InReplyToMessageId { get; init; }

    public Guid? MessageAttemptId { get; init; }

    public string? MessageActor { get; init; }

    public string? MessageProvenance { get; init; }

    /// <summary>Whether the public message-evidence route was compared with the stored attempt on provider, role, outcome, process
    /// and recorded inputs, and with what result.</summary>
    public EvidenceRouteComparisonStatus EvidenceRouteComparison { get; init; }

    /// <summary>The comparison was performed and agreed; derived, so it can never disagree with <see cref="EvidenceRouteComparison"/>.
    /// False alone does not say whether a comparison disagreed or was never attempted.</summary>
    public bool EvidenceRouteAgrees => EvidenceRouteComparison == EvidenceRouteComparisonStatus.Agrees;

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
