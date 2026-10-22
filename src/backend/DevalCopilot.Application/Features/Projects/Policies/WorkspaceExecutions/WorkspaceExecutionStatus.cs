namespace DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;

/// <summary>The closed, derived reading of who may execute in a project's owned workspace (ADR-0057). It states only what one fresh
/// read of existing durable rows found; it is never permission, provider readiness or current source attestation.</summary>
public enum WorkspaceExecutionStatus
{
    /// <summary>The project has no prepared workspace.</summary>
    NoWorkspace = 0,

    /// <summary>A coherent Ready workspace with an active lease and no competing execution owner.</summary>
    Idle = 1,

    /// <summary>Exactly one Agent attempt of the workspace is unresolved from its claim.</summary>
    AgentAttemptActive = 2,

    /// <summary>Exactly one verification execution of the workspace is unresolved from its claim.</summary>
    VerificationActive = 3,

    /// <summary>ADR-0029's reservation holds the workspace for an explicit local commit.</summary>
    CommitReserved = 4,

    /// <summary>More than one execution owner is recorded; no winner is chosen.</summary>
    Conflicted = 5,

    /// <summary>Ownership cannot be judged safely from the recorded facts.</summary>
    Unavailable = 6,

    /// <summary>ADR-0062: a queued local execution cycle holds the workspace with one reserved, unclaimed step preparation. It is not an
    /// Agent or verification process.</summary>
    PreparationReserved = 7,
}
