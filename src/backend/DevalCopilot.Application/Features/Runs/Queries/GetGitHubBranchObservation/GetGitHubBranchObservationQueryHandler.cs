using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryObservation;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;

/// <summary>
/// Observes the GitHub repository identity and the exact recorded branch tip of a run's recorded ADR-0040 target on explicit request
/// (ADR-0042). It reuses the unchanged source and target readers, invokes the observer only for a coherent recorded scope and a
/// current, successful, direct-executable GitHubCli snapshot (never discovering, refreshing or installing anything), re-reads the scope
/// after the external work and refuses the answer when it was lost, changed or became incoherent, and independently validates what the
/// port returned: the repository identity, an exact branch ref and a well-formed commit, whose ordinal comparison with the recorded
/// local commit alone decides between a match and a difference. It writes no row, event or lock and holds no transaction across the
/// external call; faults and cancellation propagate.
/// </summary>
public sealed class GetGitHubBranchObservationQueryHandler(
    IDevalCopilotDbContext dbContext, IGitHubBranchObserver observer, TimeProvider timeProvider)
    : IQueryHandler<GetGitHubBranchObservationQuery, Result<GetGitHubBranchObservationQueryResult>>
{
    public async Task<Result<GetGitHubBranchObservationQueryResult>> HandleAsync(
        GetGitHubBranchObservationQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == query.RunId, cancellationToken))
        {
            return Result<GetGitHubBranchObservationQueryResult>.Failure(GitHubBranchObservationErrors.NotFound());
        }

        var scope = await ReadScopeAsync(query.RunId, cancellationToken);
        if (scope.Source is null || scope.Target is null)
        {
            var status = scope.IsRecorded ? GitHubBranchObservationStatus.Unavailable : GitHubBranchObservationStatus.NotRecorded;
            return Reply(query.RunId, status, null, null, null);
        }

        var sourceView = scope.Source.ToView();
        var target = scope.Target;
        var snapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.GitHubCli, cancellationToken);
        if (!IsUsableTarget(snapshot, timeProvider.GetUtcNow()))
        {
            return Reply(query.RunId, GitHubBranchObservationStatus.CliUnavailable, sourceView, target, null);
        }

        var probe = await observer.ObserveAsync(
            snapshot!.ResolvedExecutablePath!, target.Owner, target.Repository, scope.Source.BranchName, cancellationToken);

        var after = await ReadScopeAsync(query.RunId, cancellationToken);
        if (after.Source != scope.Source || after.Target != target)
        {
            return Reply(query.RunId, GitHubBranchObservationStatus.Unavailable, null, null, null);
        }

        if (!IsAcceptedProbe(probe, target, scope.Source))
        {
            return Reply(query.RunId, GitHubBranchObservationStatus.Unknown, sourceView, target, null);
        }

        var observed = new GitHubBranchObserved(probe!.NodeId, probe.Owner, probe.Repository, timeProvider.GetUtcNow(), probe.Ref);
        var comparison = probe.Ref is null
            ? GitHubBranchObservationStatus.RefNotReturned
            : string.Equals(probe.Ref.CommitSha, scope.Source.CommitSha, StringComparison.Ordinal)
                ? GitHubBranchObservationStatus.CommitMatched
                : GitHubBranchObservationStatus.CommitDifferent;

        return Reply(query.RunId, comparison, sourceView, target, observed);
    }

    private static Result<GetGitHubBranchObservationQueryResult> Reply(
        Guid runId,
        GitHubBranchObservationStatus status,
        GitHubObservationSourceView? source,
        GitHubObservationTargetView? target,
        GitHubBranchObserved? observation) =>
        Result<GetGitHubBranchObservationQueryResult>.Success(
            new GetGitHubBranchObservationQueryResult(runId, status, source, target, observation));

    /// <summary>The coherent source and target, or neither; <c>IsRecorded</c> distinguishes an absent record from an incoherent one.</summary>
    private async Task<Scope> ReadScopeAsync(Guid runId, CancellationToken cancellationToken)
    {
        var source = await GitHubObservationSourceReader.ReadAsync(dbContext, runId, cancellationToken);
        var reading = await GitHubObservationTargetReader.ReadAsync(dbContext, runId, source, cancellationToken);

        return reading.Kind == GitHubObservationTargetReadingKind.Coherent && source is not null && reading.Target is not null
            ? new Scope(true, source, reading.Target)
            : new Scope(reading.Kind != GitHubObservationTargetReadingKind.None, null, null);
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

    /// <summary>The port is validated again here: a conservative opaque ID, supported names equal to the recorded scope ignoring only
    /// ASCII case (the supported names are ASCII, so ordinal-ignore-case is exactly that), and a ref that is absent or has exactly the
    /// recorded owned branch name and a well-formed commit.</summary>
    private static bool IsAcceptedProbe(GitHubBranchProbe? probe, GitHubObservationTargetView target, GitHubObservationSource source) =>
        probe is not null
        && GitHubRepositoryNodeIdPolicy.IsNodeId(probe.NodeId)
        && GitHubRepositoryNamePolicy.IsOwner(probe.Owner)
        && GitHubRepositoryNamePolicy.IsRepository(probe.Repository)
        && string.Equals(probe.Owner, target.Owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(probe.Repository, target.Repository, StringComparison.OrdinalIgnoreCase)
        && (probe.Ref is null
            || (GitHubOwnedBranchNamePolicy.IsOwnedBranch(probe.Ref.BranchName)
                && string.Equals(probe.Ref.BranchName, source.BranchName, StringComparison.Ordinal)
                && GitHubObservationTarget.IsCommitSha(probe.Ref.CommitSha)));

    private sealed record Scope(bool IsRecorded, GitHubObservationSource? Source, GitHubObservationTargetView? Target);
}
