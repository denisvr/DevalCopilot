using System.Text.Json;
using System.Text.Json.Serialization;
using DevalCopilot.Api.Startup;

namespace DevalCopilot.Api.Security.Bootstrap;

/// <summary>
/// The single-line, uniquely prefixed marker this process writes to stdout exactly once,
/// after Kestrel has bound its ephemeral loopback port. Console logging is redirected to
/// stderr in bootstrap mode (see Program.cs), so this is the only line ever written to
/// stdout — the shell does not need to disambiguate it from ordinary log output.
/// ADR-0072: the line also carries the closed startup warning categories as startup metadata; they never change the port, the
/// session or any authority. A fatal startup failure writes <see cref="StartupFailureFrame"/> instead and never this marker.
/// </summary>
public static class BootstrapReadyMarker
{
    public const string Prefix = "DEVALCOPILOT_SIDECAR_READY ";

    public static string Format(int port) => Format(port, []);

    public static string Format(int port, IReadOnlyList<StartupWarningCategory> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        var names = warnings.Distinct().Order().Select(warning => warning.ToString()).ToArray();
        return Prefix + JsonSerializer.Serialize(new ReadyBody(port, names));
    }

    private sealed record ReadyBody(
        [property: JsonPropertyName("port")] int Port,
        [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);
}
