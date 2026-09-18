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
            ParticipantKind.Claude,
            RunEventType.CodexProposal,
            CollaborationMessageType.Proposal,
            null,
            "Add attempt, stage, and run token budgets to the authoritative host, then expose current consumption through the cockpit projection.",
            "{\"scope\":\"Run and attempt budget projection\",\"implementationSteps\":\"Add the budget fields then the cockpit projection\",\"risks\":\"A command could hold a transaction too long\",\"verificationPlan\":\"Exercise the cockpit projection\",\"escalationPoints\":\"None expected\"}"),
        new SimulatedAgentStep(
            RunStage.Critique,
            ParticipantKind.Claude,
            ParticipantKind.Codex,
            RunEventType.ClaudeChallenge,
            CollaborationMessageType.Challenge,
            0,
            "The simulated attempt must still be claimed by a hosted supervisor outside the command that recorded intent, or the durable-intent invariant is violated.",
            "{\"disputedItem\":\"Claim timing\",\"materialImpact\":\"A crash could lose durable intent\",\"reasoning\":\"External work cannot occur inside the intent transaction\",\"alternativeOrQuestion\":\"Claim after the intent transaction commits\"}"),
        new SimulatedAgentStep(
            RunStage.Resolution,
            ParticipantKind.Codex,
            ParticipantKind.Claude,
            RunEventType.CodexResolution,
            CollaborationMessageType.Decision,
            1,
            "Accepted. The hosted supervisor claims the attempt only after the recording transaction commits.",
            "{\"resolution\":\"Accepted\",\"rationale\":\"Durable intent precedes external work\",\"resultingPlanChanges\":\"The hosted supervisor claims recorded work\",\"nextAction\":\"Run the deterministic sequence\"}"),
        new SimulatedAgentStep(
            RunStage.Execute,
            ParticipantKind.Claude,
            ParticipantKind.Codex,
            RunEventType.ClaudeExecution,
            CollaborationMessageType.ExecutionReport,
            2,
            "Applying the resolved plan in the simulated worktree.",
            "{\"completedWork\":\"Simulated implementation step recorded\",\"verification\":\"No provider process was invoked\"}"),
    ];

    public IReadOnlyList<SimulatedAgentStep> GetSteps() => Steps;
}
