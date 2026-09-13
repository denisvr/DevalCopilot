using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// The fixed, closed per-capability discovery descriptors for Increment 2. Candidate names,
/// fallback directories, and probe arguments are all literal constants — none of them are ever
/// derived from project content, user input, or a shell.
/// </summary>
internal static class HostCapabilityCatalog
{
    public static CapabilityProbeDescriptor Get(Capability capability) => capability switch
    {
        Capability.Git => new CapabilityProbeDescriptor
        {
            CandidateExecutableNames = ["git.exe"],
            FallbackDirectories = ProgramFilesPaths(@"Git\cmd", @"Git\bin"),
            ProbeArguments = ["--version"],
        },
        Capability.DotNetSdk => new CapabilityProbeDescriptor
        {
            CandidateExecutableNames = ["dotnet.exe"],
            FallbackDirectories = ProgramFilesPaths("dotnet"),
            ProbeArguments = ["--version"],
        },
        Capability.Node => new CapabilityProbeDescriptor
        {
            CandidateExecutableNames = ["node.exe"],
            FallbackDirectories = ProgramFilesPaths("nodejs"),
            ProbeArguments = ["--version"],
        },
        Capability.GitHubCli => new CapabilityProbeDescriptor
        {
            CandidateExecutableNames = ["gh.exe"],
            FallbackDirectories = ProgramFilesPaths("GitHub CLI"),
            ProbeArguments = ["--version"],
        },
        Capability.Docker => new CapabilityProbeDescriptor
        {
            CandidateExecutableNames = ["docker.exe"],
            FallbackDirectories = ProgramFilesPaths(@"Docker\Docker\resources\bin"),
            ProbeArguments = ["--version"],
        },
        // Codex and Claude Code CLIs are typically installed as global npm packages rather than
        // into a fixed Program Files directory, so their only reliable fallback is the user's
        // npm global-install location. Neither the exact executable name nor this fallback is
        // authoritative beyond this slice; both are centrally declared here and can be revised
        // in one place.
        //
        // Deliberately probes only a direct ".exe" candidate in this Increment 2 slice, never a
        // ".cmd" shim: running a batch/cmd shim safely means invoking it through cmd.exe, which
        // is a distinct, more complex execution boundary this slice does not implement (no
        // shell, no PATHEXT, no npm-shim support here — see HostExecutableResolver). A CLI
        // installed only as an npm ".cmd" shim is therefore truthfully reported as unavailable
        // for now, not misreported as an unrelated resolver defect. Adding shim execution is a
        // deferred compatibility decision for a future increment with its own explicit,
        // reviewed shim-execution contract.
        Capability.CodexCli => new CapabilityProbeDescriptor
        {
            CandidateExecutableNames = ["codex.exe"],
            FallbackDirectories = AppDataPaths("npm"),
            ProbeArguments = ["--version"],
        },
        Capability.ClaudeCli => new CapabilityProbeDescriptor
        {
            CandidateExecutableNames = ["claude.exe"],
            FallbackDirectories = AppDataPaths("npm"),
            ProbeArguments = ["--version"],
        },
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null),
    };

    private static IReadOnlyList<string> ProgramFilesPaths(params string[] relativePaths)
    {
        var programFiles = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);
        return relativePaths.Select(relativePath => Path.Combine(programFiles, relativePath)).ToArray();
    }

    private static IReadOnlyList<string> AppDataPaths(params string[] relativePaths)
    {
        var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        return relativePaths.Select(relativePath => Path.Combine(appData, relativePath)).ToArray();
    }
}
