namespace DevalCopilot.Application.Features.Processes.Ports;

/// <summary>
/// Runs exactly one child process to completion, timeout, or cancellation. Infrastructure
/// implements this without a command shell; no implementation of this port constructs a
/// command-line string from the request's arguments.
/// </summary>
public interface IProcessExecutionAdapter
{
    Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken);
}
