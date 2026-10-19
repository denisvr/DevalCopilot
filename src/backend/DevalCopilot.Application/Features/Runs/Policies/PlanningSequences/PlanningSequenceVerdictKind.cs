namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;

/// <summary>The kinds of decision the coordinator may take after one claimed step reached a terminal attempt (ADR-0055).</summary>
internal enum PlanningSequenceVerdictKind
{
    /// <summary>The step produced the exact, fully validated result its successor consumes.</summary>
    Continue,

    /// <summary>A critical review accepted the root or first revision: planning is ready for the separate implementation decision.</summary>
    Accepted,

    /// <summary>The depth-two revision ended at the existing Human escalation.</summary>
    Escalated,

    /// <summary>The attempt completed but its recorded evidence does not prove the expected result. Nothing continues.</summary>
    Invalid,
}
