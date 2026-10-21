namespace DevalCopilot.Application.Data;

/// <summary>
/// An independent, bounded read of whether one local-execution-sequence step's claim durably committed (ADR-0060). The step-to-operation
/// link and the operation (an Agent attempt or a verification execution) commit in one transaction, so the exact association is read
/// back through a connection that is not the claim's own: a cancellation or an exception never establishes that a claim did not commit.
/// An implementation must use an independent connection and bound how long it waits for one.
/// </summary>
public interface ILocalExecutionStepProbe
{
    Task<LocalExecutionStepProbeResult> CheckAsync(Guid stepId, CancellationToken cancellationToken);
}
