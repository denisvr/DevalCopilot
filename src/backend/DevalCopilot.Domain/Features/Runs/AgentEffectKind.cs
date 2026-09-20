namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed execution-effect classification ADR-0009 defines. Mutation leases, workspace
/// eligibility, interruption handling, and reconciliation depend on this classification, never on
/// a provider name or an incidental role check. A third value is added only when an implemented
/// capability demonstrates that these two are insufficient.
/// </summary>
public enum AgentEffectKind
{
    /// <summary>The role's attempt is not authorized to mutate the owned workspace. A dispatched
    /// attempt found still Running at restart therefore does not require mutation-ambiguity
    /// reconciliation — it is always safe to simply mark it Interrupted. An unexpected fingerprint
    /// change observed for a ReadOnly attempt is always external drift, never this attempt's own
    /// doing. This classification makes no claim about the absence of a bounded provider process,
    /// provider network access, artifact I/O, or orchestrator-owned Git evidence operations —
    /// those remain governed by separate adapter and permission boundaries, not by this
    /// enum.</summary>
    ReadOnly = 0,

    /// <summary>The role's attempt is authorized to modify the owned workspace. A dispatched
    /// attempt found still Running at restart may have modified it before the host was lost, and
    /// therefore requires evidence-aware reconciliation (independently re-reading fresh Git
    /// evidence) rather than an unconditional Interrupt. Whether an actual change is required for
    /// this role's own successful (Completed) outcome is determined by its own response contract
    /// and completion transition — this classification does not itself claim that every
    /// WorkspaceMutating role must always change the fingerprint to succeed.</summary>
    WorkspaceMutating = 1,
}
