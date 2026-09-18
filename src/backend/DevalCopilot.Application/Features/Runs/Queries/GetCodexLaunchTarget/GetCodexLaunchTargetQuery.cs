using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCodexLaunchTarget;

/// <summary>
/// The durable, currently successful Codex launch target — read directly from the
/// <c>HostCapabilitySnapshot</c> the provider-runtime-preflight slice maintains, never from a
/// fresh PATH or private-desktop-application search. <see langword="null"/> when Codex is not
/// currently observed as available. The caller (the Codex adapter) still independently
/// revalidates that these components exist and retain their accepted shape immediately before
/// invocation — this query never performs filesystem I/O itself.
/// </summary>
public sealed record GetCodexLaunchTargetQuery : IQuery<CodexLaunchTarget?>;

public sealed record CodexLaunchTarget(string ExecutablePath, string? ScriptPath);
