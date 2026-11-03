using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The closed projector of one <c>claude auth status</c> result. The bounded in-memory output is parsed only when the child exited
/// with complete, untruncated output, and only three documented members are read: the boolean <c>loggedIn</c> (which must agree with
/// the documented exit code, 0 authenticated and 1 logged out), the method name (mapped to a closed token) and the optional
/// <c>configDirectory</c> (reduced to a one-way fingerprint). Duplicated or mistyped members, other exit codes, truncation and every
/// other shape are <see cref="ClaudeStatusState.Unknown"/>. No other member is read and nothing parsed is kept or rendered.
/// </summary>
public static class ClaudeStatusProjector
{
    /// <summary>The most output bytes held per stream and in total for one status child.</summary>
    public const int MaximumStreamBytes = 16 * 1024;

    private const int MaximumJsonDepth = 16;

    public static ClaudeStatusObservation Project(string context, ProcessExecutionResult result)
    {
        var physical = result.Outcome switch
        {
            ProcessExecutionOutcome.Exited => ClaudeStatusPhysical.Exited,
            ProcessExecutionOutcome.TimedOut => ClaudeStatusPhysical.TimedOut,
            ProcessExecutionOutcome.Cancelled => ClaudeStatusPhysical.Cancelled,
            _ => ClaudeStatusPhysical.StartFailed,
        };
        var observation = new ClaudeStatusObservation(
            context,
            physical,
            physical == ClaudeStatusPhysical.Exited ? result.ExitCode : null,
            (long)result.Duration.TotalMilliseconds,
            result.IoCompletion.StandardOutput.ToString(),
            result.IoCompletion.StandardError.ToString(),
            result.StandardOutputTruncated,
            result.StandardErrorTruncated,
            ClaudeStatusState.Unknown,
            "unknown",
            null);
        return observation.IsCoherentOutput() ? Parse(observation, result) : observation;
    }

    private static bool IsCoherentOutput(this ClaudeStatusObservation observation) =>
        observation is
        {
            Physical: ClaudeStatusPhysical.Exited,
            ExitCode: 0 or 1,
            StandardOutputTruncated: false,
            StandardErrorTruncated: false,
        }
        && observation.IoComplete;

    private static ClaudeStatusObservation Parse(ClaudeStatusObservation observation, ProcessExecutionResult result)
    {
        try
        {
            var options = new JsonDocumentOptions { MaxDepth = MaximumJsonDepth };
            using var document = JsonDocument.Parse(result.StandardOutput, options);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return observation;
            }

            bool? loggedIn = null;
            string? method = null;
            string? directory = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in document.RootElement.EnumerateObject())
            {
                if (member.Name is not ("loggedIn" or "authMethod" or "configDirectory"))
                {
                    continue;
                }

                if (!seen.Add(member.Name))
                {
                    return observation;
                }

                switch (member.Name)
                {
                    case "loggedIn" when member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        loggedIn = member.Value.GetBoolean();
                        break;
                    case "authMethod" when member.Value.ValueKind == JsonValueKind.String:
                        method = member.Value.GetString();
                        break;
                    case "configDirectory" when member.Value.ValueKind == JsonValueKind.String:
                        directory = member.Value.GetString();
                        break;
                    default:
                        // A recognized member that is present with any other type (including null) is incoherent, never ignored:
                        // only an absent optional member is distinct and tolerated.
                        return observation;
                }
            }

            var state = (loggedIn, observation.ExitCode) switch
            {
                (true, 0) => ClaudeStatusState.ReportedAuthenticated,
                (false, 1) => ClaudeStatusState.ReportedLoggedOut,
                _ => ClaudeStatusState.Unknown,
            };
            return state == ClaudeStatusState.Unknown
                ? observation
                : observation with { State = state, AuthMethod = MethodToken(method), ConfigDirectoryFingerprint = Fingerprint(directory) };
        }
        catch (JsonException)
        {
            return observation;
        }
    }

    private static string MethodToken(string? method) => method switch
    {
        null => "unreported",
        "none" => "none",
        "claude.ai" => "claudeAi",
        "console" => "console",
        _ => "other",
    };

    private static string? Fingerprint(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || directory.Length > 1024)
        {
            return null;
        }

        var normalized = Path.TrimEndingDirectorySeparator(directory.Replace('/', '\\')).ToLowerInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
