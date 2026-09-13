namespace DevalCopilot.Api.Security.Cors;

/// <summary>
/// The fixed, non-configurable browser origins this host ever grants CORS to. Centralized
/// here rather than scattered as string literals wherever a policy is built.
///
/// The Tauri shell's bundled WebView is a separate browser origin from this loopback API,
/// not a same-origin caller: on Windows, Tauri serves the packaged frontend through its own
/// custom protocol handler at <see cref="PackagedWindowsWebView"/>, and `tauri dev`
/// navigates the real WebView directly to the Vite dev server at
/// <see cref="TauriDevelopmentWebView"/> instead. Both are launched with the same
/// <c>--bootstrap-stdin</c> sidecar mode, so both are trusted whenever that mode is active —
/// the host cannot otherwise distinguish which of the two actually started it, and both are
/// exactly the shell this host was launched by, never an arbitrary page.
///
/// CORS only widens which origins may *receive a response*; it is never authorization by
/// itself. Every protected request still requires the correct per-launch Bearer secret —
/// an allowed origin with a missing or incorrect secret still gets 401.
/// </summary>
public static class TrustedFrontendOrigins
{
    /// <summary>
    /// Fixed origin Tauri's custom protocol handler uses to serve bundled assets on
    /// Windows in a packaged build. Never a same-origin request from this API's own
    /// perspective — it is a genuinely separate browser origin.
    /// </summary>
    public const string PackagedWindowsWebView = "http://tauri.localhost";

    /// <summary>
    /// Must match <c>build.devUrl</c> in src-tauri/tauri.conf.json exactly: `tauri dev`
    /// points the real WebView at this Vite dev server instead of the packaged protocol.
    /// </summary>
    public const string TauriDevelopmentWebView = "http://localhost:5173";
}
