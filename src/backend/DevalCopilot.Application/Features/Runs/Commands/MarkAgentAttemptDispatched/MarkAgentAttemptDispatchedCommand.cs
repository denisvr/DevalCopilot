using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;

/// <summary>
/// The execution-start claim: commits, in one short transaction, that this Agent attempt's
/// provider is about to be invoked — before the hosted supervisor ever calls the Codex adapter.
/// If this fails, the supervisor must not invoke the adapter; if it succeeds, the attempt is
/// never eligible for dispatch again. <paramref name="ExpectedDirectGuidance"/> is supplied by the mutation
/// supervisors with the snapshot their feed projected; an attempt that recorded direct guidance is not dispatched
/// without it, and the fresh persisted snapshot must equal it.
/// </summary>
public sealed record MarkAgentAttemptDispatchedCommand(
    Guid RunId, Guid AttemptId, ExpectedDirectHumanGuidance? ExpectedDirectGuidance = null) : ICommand<Result<DateTimeOffset>>;
