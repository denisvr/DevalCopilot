namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputs;

/// <summary>The closed state of one attempt's recorded collaboration inputs. <see cref="Unavailable"/> covers an unprovable attempt
/// identity, an input set larger than this inspection reads and an incoherent set, and discloses neither which nor a target; it is not
/// an empty prompt, a failed invocation or proof that the stored set is invalid. <see cref="Empty"/> is reachable only for a coherent
/// Planner attempt. <see cref="Recorded"/> is a coherent, nonempty set.</summary>
public enum AgentAttemptInputsStatus
{
    Unavailable,
    Empty,
    Recorded,
}
