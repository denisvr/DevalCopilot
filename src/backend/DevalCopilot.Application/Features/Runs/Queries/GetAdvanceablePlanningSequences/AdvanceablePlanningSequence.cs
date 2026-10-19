using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAdvanceablePlanningSequences;

public sealed record AdvanceablePlanningSequence(Guid SequenceId, Guid RunId, Guid ProjectId, PlanningSequenceState State);
