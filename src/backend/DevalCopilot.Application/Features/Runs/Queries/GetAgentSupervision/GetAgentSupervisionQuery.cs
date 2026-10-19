using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;

/// <summary>
/// The current host supervision of the run's one selected Agent attempt (ADR-0056): the unique running Agent attempt, otherwise the latest
/// recorded one by attempt number. Several running Agent attempts or incoherent facts are Unavailable, never an arbitrary winner. A
/// missing receipt is NotObserved, never a synthesized heartbeat. It applies the same policy as the sweep immediately, so an elapsed
/// deadline reads as attention before it is persisted, and it writes nothing. An unknown run is NotFound.
/// </summary>
public sealed record GetAgentSupervisionQuery(Guid RunId) : IQuery<Result<GetAgentSupervisionQueryResult>>;
