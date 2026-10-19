namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputs;

/// <summary>The closed state of one attempt's recorded outputs. <see cref="Unavailable"/> covers an unprovable attempt identity, an
/// output set larger than this inspection reads and an incoherent association, and discloses neither which nor a target; it is not an
/// empty output, a failed invocation or proof that stored data is invalid. <see cref="Empty"/> means only that no associated message was
/// recorded in this read. <see cref="Recorded"/> is a coherent, nonempty set.</summary>
public enum AgentAttemptOutputsStatus
{
    Unavailable,
    Empty,
    Recorded,
}
