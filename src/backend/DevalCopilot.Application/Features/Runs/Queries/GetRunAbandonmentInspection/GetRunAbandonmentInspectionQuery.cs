using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunAbandonmentInspection;

/// <summary>
/// The recorded Human abandonment of one run (ADR-0071), proved from one bounded database statement over the registered run and project
/// and the run's exact stored-key <c>run.abandoned</c> events. There is no present eligibility, no live status, no blocker read, no
/// Git, artifact or external read and no write. An unknown run and a run whose registered project is absent fail with the fixed
/// <c>run_abandonment_inspection.not_found</c>; a recognized run without any abandonment fact is NotRecorded and abandonment evidence
/// that cannot be proved is Unavailable, never absent.
/// </summary>
public sealed record GetRunAbandonmentInspectionQuery(Guid RunId) : IQuery<Result<GetRunAbandonmentInspectionQueryResult>>;
