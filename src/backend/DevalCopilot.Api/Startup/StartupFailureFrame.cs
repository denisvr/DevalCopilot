using System.Text.Json;

namespace DevalCopilot.Api.Startup;

/// <summary>
/// ADR-0072: the one bounded, versioned startup-failure line a sidecar writes to its existing stdout protocol channel before it
/// exits nonzero, and the same safe diagnostic for a direct host's stderr. Both carry only the closed stage, reason and action
/// names; the shell validates the line against the same closed lists and a fixed size bound.
/// </summary>
public static class StartupFailureFrame
{
    public const string Prefix = "DEVALCOPILOT_SIDECAR_FAILED ";

    public const int Version = 1;

    /// <summary>The documented process exit code of a fatal startup recovery failure.</summary>
    public const int ExitCode = 3;

    public static string Format(StartupFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return Prefix + JsonSerializer.Serialize(new FrameBody(
            Version, failure.Stage.ToString(), failure.Reason.ToString(), failure.Action.ToString()));
    }

    public static string FormatDiagnostic(StartupFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return $"Startup failed: {failure.Code}";
    }

    private sealed record FrameBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("version")] int Version,
        [property: System.Text.Json.Serialization.JsonPropertyName("stage")] string Stage,
        [property: System.Text.Json.Serialization.JsonPropertyName("reason")] string Reason,
        [property: System.Text.Json.Serialization.JsonPropertyName("action")] string Action);
}
