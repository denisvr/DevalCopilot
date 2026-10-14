namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>What <see cref="IGitHubRepositoryIdentityObserver"/> reports for an observed repository: the opaque node identifier and the
/// names GitHub returned. Provider-neutral and already reduced to these three fields; nothing else crosses the port.</summary>
public sealed record GitHubRepositoryIdentity(string NodeId, string Owner, string Repository);
