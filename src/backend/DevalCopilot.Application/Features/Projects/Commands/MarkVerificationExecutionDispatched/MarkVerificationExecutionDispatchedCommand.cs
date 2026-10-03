using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;

/// <summary>Manual transaction: the single-use dispatch marker is decided and committed inside one short write-locked
/// transaction that re-reads the execution and its ownership and requires agreement with the expected snapshot.</summary>
public sealed record MarkVerificationExecutionDispatchedCommand(Guid VerificationExecutionId, VerificationDispatchSnapshot Expected)
    : IManualTransactionCommand<Result>;
