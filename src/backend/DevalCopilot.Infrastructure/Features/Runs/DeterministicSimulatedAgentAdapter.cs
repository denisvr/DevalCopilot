using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Deterministic canned Codex/Claude collaboration content for the walking-skeleton
/// slice. A real provider adapter (Increment 4) replaces this behind the same port;
/// the hosted supervisor and recording commands do not change shape.
/// </summary>
public sealed class DeterministicSimulatedAgentAdapter : ISimulatedAgentAdapter
{
    private static readonly IReadOnlyList<SimulatedAgentStep> Steps =
    [
        new SimulatedAgentStep(
            RunStage.Plan,
            ParticipantKind.Codex,
            RunEventType.CodexProposal,
            "Add attempt, stage, and run token budgets to the authoritative host, then expose current consumption through the cockpit projection."),
        new SimulatedAgentStep(
            RunStage.Critique,
            ParticipantKind.Claude,
            RunEventType.ClaudeChallenge,
            "The simulated attempt must still be claimed by a hosted supervisor outside the command that recorded intent, or the durable-intent invariant is violated."),
        new SimulatedAgentStep(
            RunStage.Resolution,
            ParticipantKind.Codex,
            RunEventType.CodexResolution,
            "Accepted. The hosted supervisor claims the attempt only after the recording transaction commits."),
        new SimulatedAgentStep(
            RunStage.Execute,
            ParticipantKind.Claude,
            RunEventType.ClaudeExecution,
            "Applying the resolved plan in the simulated worktree."),
    ];

    public IReadOnlyList<SimulatedAgentStep> GetSteps() => Steps;
}
