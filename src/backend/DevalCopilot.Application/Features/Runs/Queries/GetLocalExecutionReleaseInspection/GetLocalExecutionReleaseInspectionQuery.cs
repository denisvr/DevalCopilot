using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionReleaseInspection;

/// <summary>
/// The recorded quarantine-release receipt of one step of one run's local execution sequence (ADR-0067). One bounded database statement
/// proves the exact historical chain; there is no candidate, no confirmation, no external read and no current-eligibility read. An
/// absent or foreign run, a run without a sequence and a step outside it fail with the fixed
/// <c>local_execution_inspection.not_found</c>.
/// </summary>
public sealed record GetLocalExecutionReleaseInspectionQuery(Guid RunId, Guid StepId)
    : IQuery<Result<GetLocalExecutionReleaseInspectionQueryResult>>;
