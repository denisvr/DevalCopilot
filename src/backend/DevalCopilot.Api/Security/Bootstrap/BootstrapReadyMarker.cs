namespace DevalCopilot.Api.Security.Bootstrap;

/// <summary>
/// The single-line, uniquely prefixed marker this process writes to stdout exactly once,
/// after Kestrel has bound its ephemeral loopback port. Console logging is redirected to
/// stderr in bootstrap mode (see Program.cs), so this is the only line ever written to
/// stdout — the shell does not need to disambiguate it from ordinary log output.
/// </summary>
public static class BootstrapReadyMarker
{
    public const string Prefix = "DEVALCOPILOT_SIDECAR_READY ";

    public static string Format(int port) => $$"""{{Prefix}}{"port":{{port}}}""";
}
