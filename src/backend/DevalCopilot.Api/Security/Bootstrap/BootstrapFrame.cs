namespace DevalCopilot.Api.Security.Bootstrap;

/// <summary>
/// The single versioned frame the Tauri shell writes to this process's stdin exactly once
/// per launch. See <see cref="BootstrapFrameReader"/> for the framing and validation rules.
/// </summary>
public sealed record BootstrapFrame(int Version, string Secret);
