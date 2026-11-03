using DevalCopilot.Application.Features.Processes.Ports;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>What the admission boundary sees of one host-issued or launcher-issued process request: the exact executable, its literal
/// arguments and the other members a reviewed class constrains (the working directory and approved root, the environment variable
/// NAMES only, the deadline, the output sinks and whether standard input is supplied). A member that the producer did not state is
/// null, so a class that requires it never matches. Nothing here is ever printed or kept, and no environment value is ever read.</summary>
public sealed record ProcessShape(string ExecutablePath, IReadOnlyList<string> Arguments)
{
    public string? WorkingDirectory { get; init; }

    public string? ApprovedRoot { get; init; }

    public IReadOnlyList<string>? EnvironmentNames { get; init; }

    public TimeSpan? Timeout { get; init; }

    public string? StandardOutputSinkPath { get; init; }

    public string? StandardErrorSinkPath { get; init; }

    public bool? HasStandardInput { get; init; }

    public string ExecutableName => Path.GetFileName(ExecutablePath).ToLowerInvariant();

    /// <summary>The complete shape of a host request, as the production caller built it.</summary>
    public static ProcessShape From(ProcessExecutionRequest request) =>
        new(request.ExecutablePath, request.Arguments)
        {
            WorkingDirectory = request.WorkingDirectory,
            ApprovedRoot = request.ApprovedRoot,
            EnvironmentNames = [.. request.EnvironmentVariables.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)],
            Timeout = request.Timeout,
            StandardOutputSinkPath = request.StandardOutputSinkPath,
            StandardErrorSinkPath = request.StandardErrorSinkPath,
            HasStandardInput = request.StandardInput is not null,
        };

    public bool ArgumentsAre(params string[] expected) =>
        Arguments.SequenceEqual(expected, StringComparer.Ordinal);

    /// <summary>True when every environment variable name is one of <paramref name="allowed"/> (case-insensitively). A shape that
    /// did not state its environment never satisfies this.</summary>
    public bool EnvironmentNamesWithin(params string[] allowed) =>
        EnvironmentNames is { } names
        && names.All(name => allowed.Contains(name, StringComparer.OrdinalIgnoreCase));

    /// <summary>True when the stated deadline is finite, positive and at most <paramref name="ceiling"/>.</summary>
    public bool TimeoutWithin(TimeSpan ceiling) =>
        Timeout is { } timeout && timeout > TimeSpan.Zero && timeout <= ceiling;

    /// <summary>The first argument that is neither a <c>-c name=value</c> pair nor a global option, which is the Git subcommand.</summary>
    public string? GitSubcommand()
    {
        for (var index = 0; index < Arguments.Count; index++)
        {
            if (Arguments[index] == "-c")
            {
                index++;
                continue;
            }

            return Arguments[index].StartsWith('-') ? null : Arguments[index];
        }

        return null;
    }
}
