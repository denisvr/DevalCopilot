namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// A resolved, ready-to-invoke launch shape for one capability's probe. Closed to exactly the
/// two forms <see cref="ChildProcessExecutionAdapter"/> can run without a shell: a direct native
/// executable, or a JavaScript entrypoint run by a direct, fully qualified Node executable. Never
/// an arbitrary command string; never an arbitrary prefix argument list.
///
/// <para>
/// Both derived shapes validate their own components at construction — every absolute-path
/// requirement below is enforced here, not merely assumed of whatever a resolver happened to
/// produce. A caller (including a future one) cannot construct an instance carrying a blank or
/// relative path.
/// </para>
/// </summary>
internal abstract record ProviderLaunchTarget
{
    private ProviderLaunchTarget()
    {
    }

    /// <param name="ExecutablePath">Fully qualified. Carries no argument prefix.</param>
    public sealed record DirectExecutable(string ExecutablePath) : ProviderLaunchTarget
    {
        public string ExecutablePath { get; init; } = RequireAbsolutePath(ExecutablePath, nameof(ExecutablePath));
    }

    /// <param name="NodeExecutablePath">Fully qualified <c>node.exe</c> path.</param>
    /// <param name="ScriptPath">Fully qualified JavaScript entrypoint. Becomes the first
    /// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> item.</param>
    public sealed record NodeScript(string NodeExecutablePath, string ScriptPath) : ProviderLaunchTarget
    {
        public string NodeExecutablePath { get; init; } = RequireAbsolutePath(NodeExecutablePath, nameof(NodeExecutablePath));

        public string ScriptPath { get; init; } = RequireAbsolutePath(ScriptPath, nameof(ScriptPath));
    }

    private protected static string RequireAbsolutePath(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{paramName}' must be a non-blank, absolute path.", paramName);
        }

        return path;
    }
}
