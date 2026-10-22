using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAdvanceableLocalExecutionSequences;

/// <summary>
/// One bounded keyset page of the local execution sequences whose automatic authority is claimed or drains (preparing, awaiting a result,
/// stopping, or an Immediate sequence that is armed), in stable Int64 coordination order (ADR-0060, ADR-0062). A sweep fixes a high-water
/// number (the largest advancing coordination number when it starts, passed back unchanged by the host), finishes that finite range page
/// by page and only then starts a new sweep with a fresh high-water, so continuous arrivals can neither starve a revisit of old records
/// and their deadlines nor lengthen a sweep. It is only the coordinator's observation feed: it authorizes nothing and decides nothing, so
/// a stale answer is harmless because every observation re-decides from locked durable state. Waiting queued cycles have their own
/// observation.
/// </summary>
public sealed record GetAdvanceableLocalExecutionSequencesQuery(long AfterCoordinationNumber = 0, long? HighWaterCoordinationNumber = null)
    : IQuery<AdvanceableLocalExecutionSequencesPage>;
