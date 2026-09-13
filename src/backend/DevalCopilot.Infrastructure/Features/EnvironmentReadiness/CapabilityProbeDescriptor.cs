namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// Everything <see cref="ToolDiscoveryAdapter"/> needs to safely resolve and version-probe one
/// fixed catalog capability. Fully internal to Infrastructure — never exposed through the
/// <c>IToolDiscoveryAdapter</c> port, never project-owned, never user-configurable in this
/// slice.
/// </summary>
internal sealed record CapabilityProbeDescriptor
{
    /// <summary>Literal candidate executable file names, tried in order. Never a bare command
    /// name resolved by a shell — only exact file names checked with <c>File.Exists</c>.</summary>
    public required IReadOnlyList<string> CandidateExecutableNames { get; init; }

    /// <summary>Fixed absolute fallback directories checked after the host's own normalized
    /// PATH segments.</summary>
    public required IReadOnlyList<string> FallbackDirectories { get; init; }

    /// <summary>Fixed, literal probe arguments — never influenced by project content or user
    /// input.</summary>
    public required IReadOnlyList<string> ProbeArguments { get; init; }
}
