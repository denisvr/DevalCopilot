using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.AdvanceAgentSupervisionPhase;

/// <summary>The phase entered, its absolute deadline, the revision after the move, and whether this write recorded the first attention.</summary>
public sealed record AdvanceAgentSupervisionPhaseCommandResult(
    AgentSupervisionPhase Phase, DateTimeOffset PhaseDeadlineUtc, long Revision, bool AttentionRecorded);
