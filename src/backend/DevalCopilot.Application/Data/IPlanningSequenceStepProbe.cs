namespace DevalCopilot.Application.Data;

/// <summary>
/// An independent, bounded read of whether one planning-sequence step's claim durably committed (ADR-0055). The step-to-attempt link and
/// the attempt commit in one transaction, so the exact association is read back through a connection that is not the claim's own: a
/// cancellation or an exception never establishes that a claim did not commit. An implementation must use an independent connection and
/// bound how long it waits for one.
/// </summary>
public interface IPlanningSequenceStepProbe
{
    Task<PlanningSequenceStepProbeResult> CheckAsync(Guid stepId, CancellationToken cancellationToken);
}

/// <summary>The three-way outcome of an <see cref="IPlanningSequenceStepProbe"/> check. <see cref="Unresolved"/> is not "probably
/// absent": the probe could not establish the answer, or the association it read was inconsistent, and a caller must treat it exactly
/// like an unknown: no cleanup of possibly referenced artifacts, no success claim, no retry.</summary>
public enum PlanningSequenceStepProbeResult
{
    /// <summary>The step is linked to its reserved attempt and that exact attempt row exists.</summary>
    Claimed,

    /// <summary>The step is not linked and no attempt row with its reserved identity exists.</summary>
    NotClaimed,

    Unresolved,
}
