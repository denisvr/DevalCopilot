using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;

/// <summary>
/// The execution-start claim: commits, in one short transaction, that this Agent attempt's
/// provider is about to be invoked — before the hosted supervisor ever calls the Codex adapter.
/// If this fails, the supervisor must not invoke the adapter; if it succeeds, the attempt is
/// never eligible for dispatch again.
/// </summary>
public sealed record MarkAgentAttemptDispatchedCommand(Guid RunId, Guid AttemptId) : ICommand<Result<DateTimeOffset>>;
