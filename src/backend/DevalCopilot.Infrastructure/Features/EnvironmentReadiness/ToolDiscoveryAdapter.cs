using DevalCopilot.Application.Features.EnvironmentReadiness.Ports;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// Resolves and version-probes exactly one fixed catalog capability. Composed entirely on top
/// of the existing <see cref="IProcessExecutionAdapter"/> boundary — this is never a second
/// child-process execution path, only a fixed, narrower caller of the same one. Never persists
/// or logs raw probe output; only a parsed version (on success) or a closed reason (otherwise)
/// ever leaves this type.
/// </summary>
public sealed class ToolDiscoveryAdapter(IProcessExecutionAdapter processExecutionAdapter) : IToolDiscoveryAdapter
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private const int MaxCapturedBytes = 4 * 1024;

    public async Task<ToolDiscoveryResult> DiscoverAsync(Capability capability, CancellationToken cancellationToken)
    {
        var descriptor = HostCapabilityCatalog.Get(capability);
        var resolvedExecutablePath = HostExecutableResolver.TryResolve(
            descriptor.CandidateExecutableNames, descriptor.FallbackDirectories);

        if (resolvedExecutablePath is null)
        {
            return new ToolDiscoveryResult { Reason = CapabilityProbeReason.ExecutableNotFound };
        }

        var scratchDirectory = HostScratchDirectory.EnsureExists();
        var request = new ProcessExecutionRequest
        {
            ExecutablePath = resolvedExecutablePath,
            Arguments = descriptor.ProbeArguments,
            WorkingDirectory = scratchDirectory,
            ApprovedRoot = scratchDirectory,
            Timeout = ProbeTimeout,
            MaxBytesPerStream = MaxCapturedBytes,
            MaxTotalCapturedBytes = MaxCapturedBytes,
            EnvironmentVariables = new Dictionary<string, string>(),
        };

        ProcessExecutionResult result;
        try
        {
            result = await processExecutionAdapter.ExecuteAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // The adapter rejected the request or could not start the resolved executable —
            // the path genuinely exists, so this is inaccessibility, never "not found". Never
            // the exception object or its message.
            return new ToolDiscoveryResult { Reason = CapabilityProbeReason.ExecutableInaccessible };
        }

        switch (result.Outcome)
        {
            case ProcessExecutionOutcome.Cancelled:
                // Translates the adapter's non-throwing cancellation outcome into an actual
                // exception, so the caller's uniform OperationCanceledException handling (used
                // identically for host-shutdown cancellation everywhere else) applies here too.
                throw new OperationCanceledException(cancellationToken);

            case ProcessExecutionOutcome.TimedOut:
                return new ToolDiscoveryResult { Reason = CapabilityProbeReason.ProbeTimedOut };

            case ProcessExecutionOutcome.Exited when result.ExitCode != 0:
                // An unexpected non-zero exit from a fixed "--version" probe — never invents a
                // version from it, and never persists the captured output.
                return new ToolDiscoveryResult { Reason = CapabilityProbeReason.ExecutableInaccessible };
        }

        var version = ProbeOutputVersionParser.TryExtractVersion(result.StandardOutput);
        return version is null
            ? new ToolDiscoveryResult { Reason = CapabilityProbeReason.VersionProbeUnparseable }
            : new ToolDiscoveryResult
            {
                Reason = CapabilityProbeReason.None,
                ResolvedExecutablePath = resolvedExecutablePath,
                Version = version,
            };
    }
}
