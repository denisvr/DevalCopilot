using DevalCopilot.Application.Features.EnvironmentReadiness.Ports;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.Processes;

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
        var resolution = ResolveLaunchTarget(capability, descriptor);

        if (resolution.FailureReason.HasValue)
        {
            return ToolDiscoveryResult.Failed(resolution.FailureReason.Value);
        }

        return await ProbeResolvedAsync(
            capability,
            resolution.Target!,
            descriptor.ProbeArguments,
            WindowsProcessSystemRoot.Inputs.Current,
            HostScratchDirectory.EnsureExists,
            cancellationToken);
    }

    /// <summary>
    /// Probes an already-resolved launch target. Internal so the control flow after resolution —
    /// including the refusal that must precede scratch-directory creation and process dispatch —
    /// is testable with controlled OS inputs, without resolving anything from the real filesystem.
    /// <paramref name="capability"/> is the authoritative requested capability from
    /// <see cref="DiscoverAsync"/>: the Node runtime prerequisite follows the capability or a
    /// NodeScript target, never an executable's file name, extension or path.
    /// </summary>
    internal async Task<ToolDiscoveryResult> ProbeResolvedAsync(
        Capability capability,
        ProviderLaunchTarget launchTarget,
        IReadOnlyList<string> probeArguments,
        WindowsProcessSystemRoot.Inputs environmentInputs,
        Func<string> ensureScratchDirectory,
        CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string>();
        var requiresNodeRuntime = launchTarget is ProviderLaunchTarget.NodeScript || capability == Capability.Node;
        if (!WindowsProcessSystemRoot.TryAdd(environment, requiresNodeRuntime, environmentInputs))
        {
            // The Windows prerequisite for a Node runtime launch could not be derived: refused
            // before any scratch directory or process exists, never retried with the smaller map.
            return ToolDiscoveryResult.Failed(CapabilityProbeReason.ExecutableInaccessible);
        }

        var scratchDirectory = ensureScratchDirectory();
        var request = BuildProbeRequest(launchTarget, probeArguments, scratchDirectory, environment);

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
            return ToolDiscoveryResult.Failed(CapabilityProbeReason.ExecutableInaccessible);
        }

        switch (result.Outcome)
        {
            case ProcessExecutionOutcome.Cancelled:
                // Translates the adapter's non-throwing cancellation outcome into an actual
                // exception, so the caller's uniform OperationCanceledException handling (used
                // identically for host-shutdown cancellation everywhere else) applies here too.
                throw new OperationCanceledException(cancellationToken);

            case ProcessExecutionOutcome.TimedOut:
                return ToolDiscoveryResult.Failed(CapabilityProbeReason.ProbeTimedOut);

            case ProcessExecutionOutcome.Exited when result.ExitCode != 0:
                // An unexpected non-zero exit from a fixed "--version" probe — never invents a
                // version from it, and never persists the captured output.
                return ToolDiscoveryResult.Failed(CapabilityProbeReason.ExecutableInaccessible);
        }

        var version = ProbeOutputVersionParser.TryExtractVersion(result.StandardOutput);
        if (version is null)
        {
            return ToolDiscoveryResult.Failed(CapabilityProbeReason.VersionProbeUnparseable);
        }

        return launchTarget switch
        {
            ProviderLaunchTarget.DirectExecutable direct =>
                ToolDiscoveryResult.DirectExecutableSuccess(direct.ExecutablePath, version),
            ProviderLaunchTarget.NodeScript nodeScript =>
                ToolDiscoveryResult.NodeScriptSuccess(nodeScript.NodeExecutablePath, nodeScript.ScriptPath, version),
            _ => throw new InvalidOperationException($"Unknown launch target type '{launchTarget.GetType()}'."),
        };
    }

    /// <summary>
    /// Tries the direct <c>.exe</c> resolver first, preserving today's behavior for every
    /// capability unchanged. Only when that fails, and only for a capability that declares a
    /// <see cref="PackageEntrypointDescriptor"/>, falls back to the bounded npm-package
    /// entrypoint strategy. A capability with neither is reported <see
    /// cref="CapabilityProbeReason.ExecutableNotFound"/>, exactly as before this slice.
    /// </summary>
    private static LaunchTargetResolution ResolveLaunchTarget(Capability capability, CapabilityProbeDescriptor descriptor)
    {
        var directExecutablePath = HostExecutableResolver.TryResolve(descriptor.CandidateExecutableNames, descriptor.FallbackDirectories);
        if (directExecutablePath is not null)
        {
            return LaunchTargetResolution.Found(new ProviderLaunchTarget.DirectExecutable(directExecutablePath));
        }

        var packageDescriptor = HostCapabilityCatalog.GetPackageEntrypointDescriptor(capability);
        if (packageDescriptor is null)
        {
            return LaunchTargetResolution.Failed(CapabilityProbeReason.ExecutableNotFound);
        }

        var nodeDescriptor = HostCapabilityCatalog.Get(Capability.Node);
        var packageResolution = PackageEntrypointResolver.Resolve(
            packageDescriptor, nodeDescriptor.CandidateExecutableNames, nodeDescriptor.FallbackDirectories);

        return packageResolution.Kind switch
        {
            PackageEntrypointResolutionKind.Resolved => LaunchTargetResolution.Found(packageResolution.Target!),
            PackageEntrypointResolutionKind.Ambiguous => LaunchTargetResolution.Failed(CapabilityProbeReason.LaunchTargetAmbiguous),
            _ => LaunchTargetResolution.Failed(CapabilityProbeReason.ExecutableNotFound),
        };
    }

    /// <summary>
    /// Builds the exact, safe <see cref="ProcessExecutionRequest"/> for a resolved launch target:
    /// a direct executable carries no argument prefix, a Node script's entrypoint becomes the
    /// first <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> item ahead of the
    /// fixed probe arguments. Internal (rather than private) so this exact shape is directly,
    /// deterministically testable without resolving anything from the real filesystem. The
    /// environment is exactly what the caller supplies (none by default).
    /// </summary>
    internal static ProcessExecutionRequest BuildProbeRequest(
        ProviderLaunchTarget launchTarget,
        IReadOnlyList<string> probeArguments,
        string scratchDirectory,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var (executablePath, argumentPrefix) = launchTarget switch
        {
            ProviderLaunchTarget.DirectExecutable direct => (direct.ExecutablePath, (IReadOnlyList<string>)[]),
            ProviderLaunchTarget.NodeScript nodeScript => (nodeScript.NodeExecutablePath, (IReadOnlyList<string>)[nodeScript.ScriptPath]),
            _ => throw new InvalidOperationException($"Unknown launch target type '{launchTarget.GetType()}'."),
        };

        return new ProcessExecutionRequest
        {
            ExecutablePath = executablePath,
            Arguments = [.. argumentPrefix, .. probeArguments],
            WorkingDirectory = scratchDirectory,
            ApprovedRoot = scratchDirectory,
            Timeout = ProbeTimeout,
            MaxBytesPerStream = MaxCapturedBytes,
            MaxTotalCapturedBytes = MaxCapturedBytes,
            EnvironmentVariables = environment ?? new Dictionary<string, string>(),
        };
    }

    /// <summary>Either a usable launch target, or a closed failure reason — never both, never
    /// neither.</summary>
    private readonly record struct LaunchTargetResolution
    {
        public ProviderLaunchTarget? Target { get; private init; }

        public CapabilityProbeReason? FailureReason { get; private init; }

        public static LaunchTargetResolution Found(ProviderLaunchTarget target) => new() { Target = target };

        public static LaunchTargetResolution Failed(CapabilityProbeReason reason) => new() { FailureReason = reason };
    }
}
