using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>The outcome of evaluating one guard check: either the local guard permits the operation (and says nothing else: never
/// access, readiness or quota), or the bounded decision that stops it.</summary>
public sealed record CodexAccountUsageStopEvaluation(AgentCodexAccountUsageDecision? StopDecision)
{
    public bool Permits => StopDecision is null;

    public static CodexAccountUsageStopEvaluation Permitted { get; } = new((AgentCodexAccountUsageDecision?)null);
}
