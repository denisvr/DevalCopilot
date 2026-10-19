using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequence;

/// <summary>
/// The recorded status of one run's planning sequence (ADR-0055): bounded step receipts, fixed reason and next-action codes, the exact
/// final plan or escalation references when they are proved from durable identity, the immutable pinned tuple, the manual-control fence
/// and, for a run with no sequence, the arming-availability projection. It reads recorded rows only: no Git, provider or process
/// observation, no transcript, artifact body or path, and it proves no runtime readiness. An elapsed deadline is reported as attention
/// immediately, even before the coordinator persists it. An unknown run fails with <c>planning_sequence.not_found</c>.
/// </summary>
public sealed record GetPlanningSequenceQuery(Guid RunId) : IQuery<Result<GetPlanningSequenceQueryResult>>;
