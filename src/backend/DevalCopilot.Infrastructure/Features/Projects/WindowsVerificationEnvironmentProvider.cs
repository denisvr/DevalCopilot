using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// ADR-0072 as extended by ADR-0073 and ADR-0074: the one fixed Windows verification environment profile, <see cref="ProfileVersion"/>,
/// recorded on every new execution. Every dispatched verification receives exactly these keys and nothing from the host's ambient environment:
/// <list type="bullet">
/// <item>Base: <c>SystemRoot</c> (derived from the system directory boundary), <c>ComSpec</c> (the system <c>cmd.exe</c>, which approved
/// npm scripts use as their Windows script shell), <c>PATH</c> (the validated directory of the selected executable, then the system
/// directory; never an ambient, empty, relative or current-directory entry), and owned per-execution <c>TEMP</c>, <c>TMP</c>,
/// <c>USERPROFILE</c>, <c>APPDATA</c> and <c>LOCALAPPDATA</c>.</item>
/// <item>.NET, when the executable is <c>dotnet.exe</c>: <c>DOTNET_ROOT</c> (its directory), owned <c>DOTNET_CLI_HOME</c> and
/// <c>NUGET_PACKAGES</c>, <c>DOTNET_CLI_TELEMETRY_OPTOUT=1</c>, <c>DOTNET_NOLOGO=1</c>, (ADR-0073, version 2) <c>ProgramFiles(x86)</c>
/// pointing at a new, initially empty owned directory, which NuGet requires as the base of its machine-wide settings path, and
/// (ADR-0074, version 3) <c>DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false</c> and <c>DOTNET_GENERATE_ASPNET_CERTIFICATE=false</c>, which stop
/// the SDK's first-run experience from appending the owned tools directory to the persistent user <c>PATH</c> and from generating an
/// ASP.NET development certificate in the user's certificate store: both outlive the owned scratch. Neither ambient Program Files
/// value is read or inherited, and no <c>ProgramFiles</c> fallback is set. This scopes NuGet's machine configuration lookup to an
/// empty owned directory and disables two named SDK side effects; it is not filesystem or network isolation. <c>NUGET_PACKAGES</c> is the
/// owned directory with a trailing directory separator (WEP-F4), because the SDK's <c>$(NuGetPackageRoot)</c> is that value verbatim and
/// shared targets append package-relative paths to it directly; that does not change the directory, the keys or the profile version.</item>
/// <item>npm, when the executable is <c>node.exe</c> and the first argument is exactly the <c>npm-cli.js</c> bundled beside it (a direct
/// <c>npm.cmd</c> launch is refused): owned <c>npm_config_cache</c>, and <c>npm_config_userconfig</c> and <c>npm_config_globalconfig</c>
/// pointing at new empty owned files.</item>
/// </list>
/// The executable must be a fully qualified, existing <c>.exe</c> with no reparse point on its path. Each execution gets a new owned
/// scratch directory that must not already exist; it is never reused, and it is removed only on the caller's explicit request after the
/// process exited with complete I/O. Credentials, user configuration and caches are never copied. A missing prerequisite is
/// <see cref="VerificationEnvironmentPreparation.Unavailable"/> with no fallback or retry.
/// </summary>
public sealed class WindowsVerificationEnvironmentProvider : IVerificationEnvironmentProvider
{
    /// <summary>The profile recorded on every new execution (ADR-0074).</summary>
    public const string ProfileVersion = "windows-verification-v3";

    /// <summary>The ADR-0073 key set without the two ADR-0074 SDK side-effect flags. Executions that recorded it keep it exactly;
    /// nothing relabels, backfills, upgrades, replays or redispatches them, and no new execution uses it.</summary>
    public const string HistoricalProfileVersionV2 = "windows-verification-v2";

    /// <summary>The ADR-0072 key set without <c>ProgramFiles(x86)</c>. Executions that recorded it keep it exactly; nothing relabels,
    /// backfills, upgrades, replays or redispatches them, and no new execution uses it.</summary>
    public const string HistoricalProfileVersionV1 = "windows-verification-v1";

    private const string DotNetExecutable = "dotnet.exe";
    private const string NodeExecutable = "node.exe";

    private readonly WindowsProcessSystemRoot.Inputs _systemRootInputs;
    private readonly string _scratchRoot;
    private readonly Action<string>? _ownedChildCreated;

    public WindowsVerificationEnvironmentProvider()
        : this(
            WindowsProcessSystemRoot.Inputs.Current,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevalCopilot", "verification-environments"))
    {
    }

    /// <summary>Test-only seam: a caller-chosen scratch root instead of the real application data directory, so tests never write
    /// real scratch to a developer's machine state.</summary>
    public WindowsVerificationEnvironmentProvider(string scratchRoot)
        : this(WindowsProcessSystemRoot.Inputs.Current, scratchRoot)
    {
    }

    /// <summary>Test seam: controlled system-directory inputs, an owned scratch root and an observer called after each owned
    /// scratch child is created (a test throws from it to fail a partial setup).</summary>
    internal WindowsVerificationEnvironmentProvider(
        WindowsProcessSystemRoot.Inputs systemRootInputs, string scratchRoot, Action<string>? ownedChildCreated = null)
    {
        _systemRootInputs = systemRootInputs;
        _scratchRoot = Path.GetFullPath(scratchRoot);
        _ownedChildCreated = ownedChildCreated;
    }

    public VerificationEnvironmentPreparation Prepare(Guid verificationExecutionId, string executablePath, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!_systemRootInputs.IsWindows || !IsAcceptableFile(executablePath, ".exe"))
        {
            return VerificationEnvironmentPreparation.Unavailable(ProfileVersion);
        }

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!WindowsProcessSystemRoot.TryAdd(environment, requiresSystemRoot: true, _systemRootInputs)
            || !environment.TryGetValue("SystemRoot", out var systemRoot))
        {
            return VerificationEnvironmentPreparation.Unavailable(ProfileVersion);
        }

        var systemDirectory = Path.Combine(systemRoot, "System32");
        var commandShell = Path.Combine(systemDirectory, "cmd.exe");
        var toolDirectory = Path.GetDirectoryName(executablePath);
        if (!Directory.Exists(systemDirectory) || !IsAcceptableFile(commandShell, ".exe")
            || toolDirectory is null || !IsAcceptablePathEntry(toolDirectory) || !IsAcceptablePathEntry(systemDirectory))
        {
            return VerificationEnvironmentPreparation.Unavailable(ProfileVersion);
        }

        var kind = Classify(executablePath, toolDirectory, arguments);
        if (kind is null)
        {
            return VerificationEnvironmentPreparation.Unavailable(ProfileVersion);
        }

        if (!TryCreateScratch(verificationExecutionId, out var scratch))
        {
            return VerificationEnvironmentPreparation.Unavailable(ProfileVersion);
        }

        try
        {
            AddOwnedKeys(environment, scratch, commandShell, toolDirectory, systemDirectory, kind.Value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // ADR-0072 (review WEP-R5): a partially created profile is never used. TryCreateScratch proved this execution's scratch
            // new, and no process has run, so the partial scratch is removed and the environment is unavailable.
            ReleaseScratch(verificationExecutionId);
            return VerificationEnvironmentPreparation.Unavailable(ProfileVersion);
        }

        return new VerificationEnvironmentPreparation(true, ProfileVersion, environment);
    }

    /// <summary>Adds the owned per-execution keys; every created child is inside <paramref name="scratch"/>.</summary>
    private void AddOwnedKeys(
        Dictionary<string, string> environment, string scratch, string commandShell, string toolDirectory, string systemDirectory,
        ExecutableKind kind)
    {
        environment["ComSpec"] = commandShell;
        environment["PATH"] = string.Equals(
            Path.TrimEndingDirectorySeparator(toolDirectory), Path.TrimEndingDirectorySeparator(systemDirectory), StringComparison.OrdinalIgnoreCase)
            ? systemDirectory
            : toolDirectory + Path.PathSeparator + systemDirectory;
        var temporary = CreateOwnedDirectory(scratch, "temp");
        environment["TEMP"] = temporary;
        environment["TMP"] = temporary;
        environment["USERPROFILE"] = CreateOwnedDirectory(scratch, "profile");
        environment["APPDATA"] = CreateOwnedDirectory(scratch, "appdata");
        environment["LOCALAPPDATA"] = CreateOwnedDirectory(scratch, "localappdata");

        switch (kind)
        {
            case ExecutableKind.DotNet:
                environment["DOTNET_ROOT"] = toolDirectory;
                environment["DOTNET_CLI_HOME"] = CreateOwnedDirectory(scratch, "dotnet-home");
                environment["NUGET_PACKAGES"] = WithTrailingSeparator(CreateOwnedDirectory(scratch, "nuget-packages"));
                environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
                environment["DOTNET_NOLOGO"] = "1";
                environment["ProgramFiles(x86)"] = CreateOwnedDirectory(scratch, "program-files-x86");
                environment["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false";
                environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
                break;

            case ExecutableKind.Npm:
                environment["npm_config_cache"] = CreateOwnedDirectory(scratch, "npm-cache");
                environment["npm_config_userconfig"] = CreateEmptyOwnedFile(scratch, "npm-userconfig");
                environment["npm_config_globalconfig"] = CreateEmptyOwnedFile(scratch, "npm-globalconfig");
                break;
        }
    }

    public void ReleaseScratch(Guid verificationExecutionId)
    {
        var scratch = ScratchPath(verificationExecutionId);
        try
        {
            if (Directory.Exists(scratch) && !AgentInvocationScratchDirectory.PathOrAnyAncestorHasReparsePoint(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left in place; never a reason to change a recorded result.
        }
    }

    /// <summary>The kind of an accepted executable, or null when an npm launch is not exactly <c>node.exe</c> with its own bundled
    /// <c>npm-cli.js</c>.</summary>
    private static ExecutableKind? Classify(string executablePath, string toolDirectory, IReadOnlyList<string> arguments)
    {
        var leaf = Path.GetFileName(executablePath);
        if (string.Equals(leaf, DotNetExecutable, StringComparison.OrdinalIgnoreCase))
        {
            return ExecutableKind.DotNet;
        }

        if (!string.Equals(leaf, NodeExecutable, StringComparison.OrdinalIgnoreCase))
        {
            return ExecutableKind.Other;
        }

        // node.exe running anything other than an npm-cli.js script gets only the base keys; an npm-cli.js script must be exactly
        // the one bundled beside this node.exe.
        if (arguments.Count == 0 || !arguments[0].EndsWith("npm-cli.js", StringComparison.OrdinalIgnoreCase))
        {
            return ExecutableKind.Other;
        }

        var bundledCli = Path.Combine(toolDirectory, "node_modules", "npm", "bin", "npm-cli.js");
        return Path.IsPathFullyQualified(arguments[0])
            && string.Equals(Path.GetFullPath(arguments[0]), bundledCli, StringComparison.OrdinalIgnoreCase)
            && IsAcceptableFile(bundledCli, ".js")
                ? ExecutableKind.Npm
                : null;
    }

    private bool TryCreateScratch(Guid verificationExecutionId, out string scratch)
    {
        scratch = ScratchPath(verificationExecutionId);
        try
        {
            Directory.CreateDirectory(_scratchRoot);
            if (AgentInvocationScratchDirectory.PathOrAnyAncestorHasReparsePoint(_scratchRoot) || Directory.Exists(scratch))
            {
                return false;
            }

            Directory.CreateDirectory(scratch);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string ScratchPath(Guid verificationExecutionId) => Path.Combine(_scratchRoot, verificationExecutionId.ToString("N"));

    private string CreateOwnedDirectory(string scratch, string name)
    {
        var path = Directory.CreateDirectory(Path.Combine(scratch, name)).FullName;
        _ownedChildCreated?.Invoke(path);
        return path;
    }

    /// <summary>
    /// WEP-F4, a representational normalization only: the SDK derives <c>$(NuGetPackageRoot)</c> from <c>NUGET_PACKAGES</c> verbatim, and
    /// shared MSBuild targets append a package-relative path directly, so the owned package directory is supplied with its trailing
    /// directory separator. The directory, its location, its lifetime and the key set are unchanged.
    /// </summary>
    private static string WithTrailingSeparator(string directory) =>
        Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;

    private string CreateEmptyOwnedFile(string scratch, string name)
    {
        var path = Path.Combine(scratch, name);
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
        }

        _ownedChildCreated?.Invoke(path);
        return path;
    }

    private static bool IsAcceptableFile(string path, string extension) =>
        Path.IsPathFullyQualified(path)
        && string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)
        && File.Exists(path)
        && !AgentInvocationScratchDirectory.PathOrAnyAncestorHasReparsePoint(path);

    /// <summary>A PATH entry is an existing, fully qualified directory whose text cannot split or quote the variable.</summary>
    private static bool IsAcceptablePathEntry(string directory) =>
        Path.IsPathFullyQualified(directory)
        && directory.IndexOfAny([Path.PathSeparator, '"']) < 0
        && Directory.Exists(directory);

    private enum ExecutableKind
    {
        Other,
        DotNet,
        Npm,
    }
}
