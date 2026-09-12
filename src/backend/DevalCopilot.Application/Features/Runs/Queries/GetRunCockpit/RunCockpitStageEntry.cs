using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

public sealed record RunCockpitStageEntry(RunStage Stage, bool IsCompleted, bool IsActive);
