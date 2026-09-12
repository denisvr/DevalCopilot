namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The bounded, deterministic simulated-run stage sequence for the walking skeleton.
/// Ordinal order defines forward-only progression.
/// </summary>
public enum RunStage
{
    Intake = 0,
    Plan = 1,
    Critique = 2,
    Resolution = 3,
    Execute = 4,
    Completed = 5,
}
