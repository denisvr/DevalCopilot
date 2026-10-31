using System.Diagnostics.CodeAnalysis;
using DevalCopilot.Infrastructure.Features.Processes;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// The one environment allowlist shared by the three Claude <c>--print</c> adapters
/// (<see cref="ClaudeCriticalReviewAdapter"/>, <see cref="ClaudeImplementationAdapter"/> and
/// <see cref="ClaudeReviewCorrectionAdapter"/>), which intentionally share this responsibility and
/// nothing else of their role contracts. Only non-secret OS/profile-location values required to
/// locate existing local Claude Code authentication: <c>USERPROFILE</c> and <c>CLAUDE_CONFIG_DIR</c>,
/// each forwarded unchanged only when this host process already has a nonempty value, plus exactly
/// <c>SystemRoot</c> on Windows from <see cref="WindowsProcessSystemRoot"/> — never PATH, a provider
/// token, a temp/proxy/certificate entry or an arbitrary environment entry. Native Claude version
/// discovery does not use this builder.
/// </summary>
internal static class ClaudeProcessEnvironment
{
    private const string UserProfileVariable = "USERPROFILE";
    private const string ClaudeConfigDirectoryVariable = "CLAUDE_CONFIG_DIR";

    /// <summary>Returns <see langword="false"/> with no map when the Windows prerequisite cannot be
    /// derived; the caller must then fail before any manifest read, sink request, artifact write or
    /// process dispatch, never retrying with the smaller map. Never carries a value or exception
    /// detail out; cancellation from the system-directory reader propagates.</summary>
    internal static bool TryBuild(
        ClaudeProcessEnvironmentInputs inputs, [NotNullWhen(true)] out Dictionary<string, string>? environment)
    {
        var candidate = new Dictionary<string, string>();

        var userProfile = inputs.ReadVariable(UserProfileVariable);
        if (!string.IsNullOrEmpty(userProfile))
        {
            candidate[UserProfileVariable] = userProfile;
        }

        var claudeConfigDirectory = inputs.ReadVariable(ClaudeConfigDirectoryVariable);
        if (!string.IsNullOrEmpty(claudeConfigDirectory))
        {
            candidate[ClaudeConfigDirectoryVariable] = claudeConfigDirectory;
        }

        if (!WindowsProcessSystemRoot.TryAdd(candidate, requiresSystemRoot: true, inputs.SystemRoot))
        {
            environment = null;
            return false;
        }

        environment = candidate;
        return true;
    }
}
