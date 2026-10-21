using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;

/// <summary>
/// The recorded status of one run's local execution sequence (ADR-0060): bounded step receipts, fixed reason and next-action codes, the
/// exact final report, review or escalation references when they are proved from durable identity, the immutable pinned tuple, the
/// pinned and current source, the allowance, the advancement deadline and the manual-control fence. It reads recorded rows only: no Git,
/// provider or process observation, no transcript, artifact body, path or raw executable, and it proves no runtime readiness, no live
/// process and no current source. An elapsed deadline is reported as attention immediately, even before the coordinator persists it.
/// An unknown run fails with <c>local_execution_sequence.not_found</c>.
/// </summary>
public sealed record GetLocalExecutionSequenceQuery(Guid RunId) : IQuery<Result<GetLocalExecutionSequenceQueryResult>>;
