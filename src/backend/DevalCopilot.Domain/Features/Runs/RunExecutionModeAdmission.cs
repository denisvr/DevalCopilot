namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one admission table for <see cref="RunExecutionMode"/>. Every value outside the recognized set
/// is refused by every check, so an undefined stored mode never authorizes execution.
/// </summary>
public static class RunExecutionModeAdmission
{
    /// <summary>The numeric values a simulation path may execute: Simulated and Legacy.</summary>
    public static readonly IReadOnlyList<RunExecutionMode> SimulationModes =
        [RunExecutionMode.Simulated, RunExecutionMode.Legacy];

    /// <summary>The numeric values an Agent claim, feed, or dispatch may execute: ManualAgent and Legacy.</summary>
    public static readonly IReadOnlyList<RunExecutionMode> AgentModes =
        [RunExecutionMode.ManualAgent, RunExecutionMode.Legacy];

    /// <summary>The numeric values a standalone Process claim may execute: Legacy only.</summary>
    public static readonly IReadOnlyList<RunExecutionMode> ProcessModes = [RunExecutionMode.Legacy];

    public static bool AdmitsSimulation(RunExecutionMode mode) =>
        mode is RunExecutionMode.Simulated or RunExecutionMode.Legacy;

    public static bool AdmitsAgent(RunExecutionMode mode) =>
        mode is RunExecutionMode.ManualAgent or RunExecutionMode.Legacy;

    public static bool AdmitsProcess(RunExecutionMode mode) => mode is RunExecutionMode.Legacy;

    /// <summary>True only for a mode a creation operation may assign.</summary>
    public static bool IsAssignableAtCreation(RunExecutionMode mode) =>
        mode is RunExecutionMode.Simulated or RunExecutionMode.ManualAgent;

    public static bool IsRecognized(RunExecutionMode mode) =>
        mode is RunExecutionMode.Legacy or RunExecutionMode.Simulated or RunExecutionMode.ManualAgent;
}
