using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;

public sealed record ClaimVerificationExecutionCommand(Guid ProjectId, Guid VerificationCommandId, Guid GitCheckpointId)
    : IManualTransactionCommand<Result<ClaimVerificationExecutionCommandResult>>;
