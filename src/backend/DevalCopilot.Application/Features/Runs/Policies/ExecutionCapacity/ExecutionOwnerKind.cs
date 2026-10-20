namespace DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

/// <summary>Which existing table a counted owner lives in (ADR-0058). An identifier is only ever compared inside its own kind.</summary>
internal enum ExecutionOwnerKind
{
    /// <summary>A coherent unresolved Agent attempt whose Domain contract is WorkspaceMutating.</summary>
    AgentAttempt = 0,

    /// <summary>An unresolved verification execution.</summary>
    Verification = 1,

    /// <summary>ADR-0029's reservation, identified by its one open local-commit operation.</summary>
    LocalCommit = 2,
}
