using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

public sealed record GetRunCockpitQueryResult(
    Guid RunId,
    Guid ProjectId,
    string ProjectName,
    int ExecutionNumber,
    string Objective,
    RunLifecycle Lifecycle,
    RunStage Stage,
    ParticipantIdentity ActiveParticipant,
    double AutonomousDurationSeconds,
    long LatestSequence,
    IReadOnlyList<RunCockpitStageEntry> StageMap,
    bool CanPause,
    bool CanStop,
    RunCockpitTokenUsageSummary TokenUsageSummary,
    int MaximumAgentAttempts,
    int AgentAttemptsUsed,
    bool AgentBudgetExhausted,
    RunCockpitAgentInvocationTimeBudgetSummary AgentInvocationTimeBudget,
    RunCockpitAgentAttemptEntry? LatestAgentAttempt = null);
