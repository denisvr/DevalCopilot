using System.Collections.Concurrent;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Infrastructure.Features.Processes;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// A process executor for the guard regressions over the sealed scene. Every request whose executable is not Git is recorded in
/// <see cref="Attempts"/> (executable file name and first argument only, never a variable value) and refused at once: no such process is
/// ever started, so a port that should have been refused but still reached a production adapter (such as a GitHub CLI observer) is
/// counted here instead of running. The host's own read-only Git inspection, which ADR-0046 keeps on the real adapters and which a host
/// over the sealed scene performs by itself, passes through to the real executor unchanged and is counted in <see cref="GitReads"/>, so
/// the composed host behaves exactly like every other clone except for the refusal.
/// </summary>
internal sealed class FailOnUseProcessExecutionAdapter : IProcessExecutionAdapter
{
    private readonly ChildProcessExecutionAdapter _git = new();

    public ConcurrentQueue<string> Attempts { get; } = new();

    public ConcurrentQueue<string> GitReads { get; } = new();

    public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
    {
        var name = $"{Path.GetFileName(request.ExecutablePath)} {request.Arguments.FirstOrDefault()}";
        if (string.Equals(Path.GetFileNameWithoutExtension(request.ExecutablePath), "git", StringComparison.OrdinalIgnoreCase))
        {
            GitReads.Enqueue(name);
            return _git.ExecuteAsync(request, cancellationToken);
        }

        Attempts.Enqueue(name);
        throw new InvalidOperationException("The fail-on-use process executor refuses every process start other than Git.");
    }
}
