using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionStepInspection;

/// <summary>
/// The selected operation of one recorded step (ADR-0061). It re-proves the bounded structure and the exact step on every request. An
/// unknown run, a run without a sequence and a step outside that run's sequence fail with <c>local_execution_inspection.not_found</c>.
/// </summary>
public sealed record GetLocalExecutionStepInspectionQuery(Guid RunId, Guid StepId)
    : IQuery<Result<GetLocalExecutionStepInspectionQueryResult>>;
