using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetGitHubStoredAuthentication;

/// <summary>
/// Reads the shared GitHubCli discovery snapshot untracked (this operation owns its own five-line lookup, like the
/// Codex observations) and invokes the observer only for a current, successful, coherent direct-executable target
/// under the readiness freshness rule. Anything else is <see cref="GitHubStoredAuthenticationStatus.CliUnavailable"/>
/// with nothing invoked; this query never discovers, refreshes, installs or writes.
/// </summary>
public sealed class GetGitHubStoredAuthenticationQueryHandler(
    IDevalCopilotDbContext dbContext, IGitHubStoredAuthenticationObserver observer, TimeProvider timeProvider)
    : IQueryHandler<GetGitHubStoredAuthenticationQuery, GetGitHubStoredAuthenticationQueryResult>
{
    public async Task<GetGitHubStoredAuthenticationQueryResult> HandleAsync(
        GetGitHubStoredAuthenticationQuery query, CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.GitHubCli, cancellationToken);

        if (!IsUsableTarget(snapshot, timeProvider.GetUtcNow()))
        {
            return GetGitHubStoredAuthenticationQueryResult.CliUnavailable;
        }

        var observation = await observer.ObserveAsync(snapshot!.ResolvedExecutablePath!, cancellationToken);

        return Normalize(observation);
    }

    private static bool IsUsableTarget(HostCapabilitySnapshot? snapshot, DateTimeOffset nowUtc) =>
        snapshot is not null
        && snapshot.ReasonCode == CapabilityProbeReason.None
        && snapshot.LaunchKind == CapabilityLaunchKind.DirectExecutable
        && snapshot.ResolvedScriptPath is null
        && !string.IsNullOrWhiteSpace(snapshot.ResolvedExecutablePath)
        && Path.IsPathFullyQualified(snapshot.ResolvedExecutablePath)
        && !string.IsNullOrWhiteSpace(snapshot.ObservedVersion)
        && snapshot.EvidenceObservedAtUtc.HasValue
        && !HostCapabilityReadinessProjector.IsStale(snapshot, nowUtc);

    /// <summary>An observer can report only a coherent observation: a time exactly for the three observed statuses.</summary>
    private static GetGitHubStoredAuthenticationQueryResult Normalize(GitHubStoredAuthenticationObservation observation) =>
        observation.Status switch
        {
            GitHubStoredAuthenticationStatus.NoActiveAccount or GitHubStoredAuthenticationStatus.Authenticated
                or GitHubStoredAuthenticationStatus.IssueReported when observation.ObservedAtUtc.HasValue =>
                new GetGitHubStoredAuthenticationQueryResult(observation.Status, observation.ObservedAtUtc),
            _ => GetGitHubStoredAuthenticationQueryResult.Unknown,
        };
}
