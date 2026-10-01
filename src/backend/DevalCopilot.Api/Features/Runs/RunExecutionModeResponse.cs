using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Features.Runs;

/// <summary>The wire names of <see cref="RunExecutionMode"/>. A stored number outside the recognized set is
/// reported as <see cref="Unrecognized"/>, never as a recognized mode.</summary>
public static class RunExecutionModeResponse
{
    public const string Unrecognized = "Unrecognized";

    public static string From(RunExecutionMode mode) => RunExecutionModeAdmission.IsRecognized(mode)
        ? mode.ToString()
        : Unrecognized;
}
