using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CompleteSimulatedRun;

/// <summary>
/// Owns its own save so the assigned monotonic event sequence can be reported back to
/// the caller immediately, for the post-commit SignalR notification.
/// </summary>
public sealed record CompleteSimulatedRunCommand(Guid RunId, Guid AttemptId) : IManualTransactionCommand<Result<long>>;
