namespace DevalCopilot.Api.Security.Bootstrap;

/// <summary>
/// A successfully parsed <see cref="BootstrapFrame"/> plus any bytes the underlying read
/// already buffered past the frame's terminating newline. The shell never writes anything
/// after the one frame, so <see cref="Trailing"/> is only ever non-empty when the input
/// violates the protocol (e.g. a duplicated frame) — callers must treat that as a protocol
/// violation, never as more bootstrap data to parse.
/// </summary>
public sealed record BootstrapReadResult(BootstrapFrame Frame, byte[] Trailing);
