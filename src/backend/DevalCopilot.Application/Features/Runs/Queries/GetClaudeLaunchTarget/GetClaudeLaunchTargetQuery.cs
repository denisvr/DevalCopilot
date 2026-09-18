using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetClaudeLaunchTarget;

/// <summary>
/// The durable, currently successful Claude Code launch target — read directly from the
/// <c>HostCapabilitySnapshot</c> the provider-runtime-preflight slice maintains, never from a
/// fresh PATH or private-desktop-application search. <see langword="null"/> when Claude Code is
/// not currently observed as available. The caller (the Claude adapter) still independently
/// revalidates that this component exists and retains its accepted shape immediately before
/// invocation — this query never performs filesystem I/O itself.
/// </summary>
public sealed record GetClaudeLaunchTargetQuery : IQuery<ClaudeLaunchTarget?>;

/// <summary>Never a script path: the resolver only ever accepts Claude's launch target as a
/// <c>DirectExecutable</c> (its shipped native <c>claude.exe</c>) — see
/// <c>PackageEntrypointResolver</c>'s reparse/PE-header revalidation.</summary>
public sealed record ClaudeLaunchTarget(string ExecutablePath);
