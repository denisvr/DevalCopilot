using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.ObserveLocalExecutionSequence;

/// <summary>The sequence state after one observation, and the ticket of the one step this call reserved (null when it reserved none).
/// The ticket is the only authority to try that step's claim, and only once.</summary>
public sealed record ObserveLocalExecutionSequenceCommandResult(
    Guid RunId, LocalExecutionSequenceState State, bool Changed, LocalExecutionStepTicket? ReservedStep);
