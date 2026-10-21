using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

namespace DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;

/// <summary>
/// <see cref="LocalStep"/> is the internal identity of a coordinator-composed verification step of an explicitly authorized local
/// execution sequence (ADR-0060). It is never bound from a request: when set, the claim uses the step's reserved execution identity,
/// requires the pinned recipe and source checkpoint, and links the step in the claim's own transaction.
/// </summary>
public sealed record ClaimVerificationExecutionCommand(Guid ProjectId, Guid VerificationCommandId, Guid GitCheckpointId)
    : IManualTransactionCommand<Result<ClaimVerificationExecutionCommandResult>>
{
    internal LocalExecutionStepTicket? LocalStep { get; init; }
}
