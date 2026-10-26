using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningStepInspection;

/// <summary>
/// The selected association of one recorded planning step (ADR-0068). It re-proves the bounded structure and the exact step membership
/// on every request. An unknown run, a run without a sequence and a step outside that run's sequence fail with
/// <c>planning_sequence_inspection.not_found</c>.
/// </summary>
public sealed record GetPlanningStepInspectionQuery(Guid RunId, Guid StepId) : IQuery<Result<GetPlanningStepInspectionQueryResult>>;
