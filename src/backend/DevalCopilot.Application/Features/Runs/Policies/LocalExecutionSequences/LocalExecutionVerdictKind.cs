namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

internal enum LocalExecutionVerdictKind
{
    /// <summary>Conclude the step and reserve the one preparation intent of its successor.</summary>
    Reserve,

    /// <summary>An ordinary review approved the exact linked report: the cycle ends awaiting the Human approval.</summary>
    Approved,

    /// <summary>The correction allowance was reached with unresolved findings: the host records its one fixed escalation.</summary>
    AllowanceReached,

    /// <summary>The provider diagnosis escalated: its exact Escalation message is preserved as the end of the cycle.</summary>
    ProviderEscalation,

    /// <summary>Advancement closes with a fixed reason; nothing is retried, repaired or skipped.</summary>
    Attention,
}
