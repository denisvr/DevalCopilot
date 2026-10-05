namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>The exact vetted Codex launch target (executable and optional script) an observation and an invocation are bound to.</summary>
public sealed record CodexAccountUsageLaunchTuple(string ExecutablePath, string? ScriptPath);
