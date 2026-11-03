using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Processes;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>An owned double of the process boundary for lot A: it answers each request with the next scripted result or exception,
/// records the requests exactly as the runner built them, and never starts a process.</summary>
public sealed class ScriptedStatusAdapter(params Func<CancellationToken, Task<ProcessExecutionResult>>[] script)
    : IProcessExecutionAdapter
{
    private readonly object _gate = new();
    private readonly List<ProcessExecutionRequest> _requests = [];
    private int _next;

    public IReadOnlyList<ProcessExecutionRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public static Func<CancellationToken, Task<ProcessExecutionResult>> Exited(int exitCode, string standardOutput) =>
        _ => Task.FromResult(Result(ProcessExecutionOutcome.Exited, exitCode, standardOutput));

    public static Func<CancellationToken, Task<ProcessExecutionResult>> Authenticated(string? configDirectory = null) =>
        Exited(0, $"{{\"loggedIn\":true,\"authMethod\":\"claude.ai\",\"email\":\"someone@example.invalid\"{ConfigMember(configDirectory)}}}");

    public static Func<CancellationToken, Task<ProcessExecutionResult>> LoggedOut(string? configDirectory = null) =>
        Exited(1, $"{{\"loggedIn\":false,\"authMethod\":\"none\"{ConfigMember(configDirectory)}}}");

    public static ProcessExecutionResult Result(
        ProcessExecutionOutcome outcome,
        int? exitCode,
        string standardOutput,
        ProcessIoCompletion? io = null,
        bool truncated = false) => new()
        {
            Outcome = outcome,
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardOutputTruncated = truncated,
            StandardError = string.Empty,
            StandardErrorTruncated = false,
            Duration = TimeSpan.FromMilliseconds(250),
            IoCompletion = io ?? ProcessIoCompletion.CompleteWithoutInput,
        };

    public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<ProcessExecutionResult>> step;
        lock (_gate)
        {
            _requests.Add(request);
            step = script[_next++];
        }

        return step(cancellationToken);
    }

    private static string ConfigMember(string? configDirectory) =>
        configDirectory is null ? string.Empty : $",\"configDirectory\":\"{configDirectory.Replace("\\", "\\\\", StringComparison.Ordinal)}\"";
}
