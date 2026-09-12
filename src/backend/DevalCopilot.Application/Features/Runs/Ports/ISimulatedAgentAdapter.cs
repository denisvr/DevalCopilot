namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Produces the bounded, deterministic step sequence a claimed simulated attempt plays
/// back. Infrastructure implements this with fixed canned content; nothing here executes
/// a real process.
/// </summary>
public interface ISimulatedAgentAdapter
{
    IReadOnlyList<SimulatedAgentStep> GetSteps();
}
