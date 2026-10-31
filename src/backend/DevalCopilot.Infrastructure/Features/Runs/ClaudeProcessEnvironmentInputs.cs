using DevalCopilot.Infrastructure.Features.Processes;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>The ambient reads a Claude <c>--print</c> invocation's environment depends on, injectable
/// so each adapter's real control flow is testable with fabricated profile values and system
/// directories, without reading or mutating a real process variable.</summary>
internal sealed record ClaudeProcessEnvironmentInputs(
    Func<string, string?> ReadVariable, WindowsProcessSystemRoot.Inputs SystemRoot)
{
    internal static ClaudeProcessEnvironmentInputs Current { get; } =
        new(System.Environment.GetEnvironmentVariable, WindowsProcessSystemRoot.Inputs.Current);
}
