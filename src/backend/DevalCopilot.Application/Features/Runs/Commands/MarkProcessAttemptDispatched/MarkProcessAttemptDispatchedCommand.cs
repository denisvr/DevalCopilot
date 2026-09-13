using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.MarkProcessAttemptDispatched;

/// <summary>
/// The execution-start claim: commits, in one short transaction, that this Process attempt's
/// external command is about to be invoked — before the hosted supervisor ever calls the
/// process-execution adapter. If this fails, the supervisor must not invoke the adapter; if it
/// succeeds, the attempt is never eligible for dispatch again, guaranteeing the external
/// command runs at most once for this attempt regardless of whether a terminal result is ever
/// later recorded.
/// </summary>
public sealed record MarkProcessAttemptDispatchedCommand(Guid RunId, Guid AttemptId) : ICommand<Result<DateTimeOffset>>;
