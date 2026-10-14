using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryObservation;

/// <summary>
/// Observes the GitHub repository identity of a run's recorded ADR-0040 target on explicit request (ADR-0041). It reuses the unchanged
/// source and target readers, invokes the observer only for a coherent recorded scope and a current, successful, direct-executable
/// GitHubCli snapshot (never discovering, refreshing or installing anything), re-reads the scope after the external work and refuses
/// the answer when it was lost, changed or became incoherent, and independently validates the identity the port returned. It writes
/// no row, event or lock and holds no transaction across the external call; faults and cancellation propagate.
/// </summary>
public sealed class GetGitHubRepositoryObservationQueryHandler(
    IDevalCopilotDbContext dbContext, IGitHubRepositoryIdentityObserver observer, TimeProvider timeProvider)
    : IQueryHandler<GetGitHubRepositoryObservationQuery, Result<GetGitHubRepositoryObservationQueryResult>>
{
    public async Task<Result<GetGitHubRepositoryObservationQueryResult>> HandleAsync(
        GetGitHubRepositoryObservationQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == query.RunId, cancellationToken))
        {
            return Result<GetGitHubRepositoryObservationQueryResult>.Failure(GitHubRepositoryObservationErrors.NotFound());
        }

        var scope = await ReadScopeAsync(query.RunId, cancellationToken);
        if (scope.Source is null || scope.Target is null)
        {
            var status = scope.IsRecorded
                ? GitHubRepositoryObservationStatus.Unavailable
                : GitHubRepositoryObservationStatus.NotRecorded;
            return Reply(query.RunId, status, null, null, null);
        }

        var sourceView = scope.Source.ToView();
        var target = scope.Target;
        var snapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.GitHubCli, cancellationToken);
        if (!IsUsableTarget(snapshot, timeProvider.GetUtcNow()))
        {
            return Reply(query.RunId, GitHubRepositoryObservationStatus.CliUnavailable, sourceView, target, null);
        }

        var identity = await observer.ObserveAsync(snapshot!.ResolvedExecutablePath!, target.Owner, target.Repository, cancellationToken);

        var after = await ReadScopeAsync(query.RunId, cancellationToken);
        if (after.Source != scope.Source || after.Target != target)
        {
            return Reply(query.RunId, GitHubRepositoryObservationStatus.Unavailable, null, null, null);
        }

        if (!IsAcceptedIdentity(identity, target))
        {
            return Reply(query.RunId, GitHubRepositoryObservationStatus.Unknown, sourceView, target, null);
        }

        var observed = new GitHubRepositoryObservedIdentity(identity!.NodeId, identity.Owner, identity.Repository, timeProvider.GetUtcNow());
        return Reply(query.RunId, GitHubRepositoryObservationStatus.Observed, sourceView, target, observed);
    }

    private static Result<GetGitHubRepositoryObservationQueryResult> Reply(
        Guid runId,
        GitHubRepositoryObservationStatus status,
        GitHubObservationSourceView? source,
        GitHubObservationTargetView? target,
        GitHubRepositoryObservedIdentity? observation) =>
        Result<GetGitHubRepositoryObservationQueryResult>.Success(
            new GetGitHubRepositoryObservationQueryResult(runId, status, source, target, observation));

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

    /// <summary>The port is validated again here: a conservative opaque ID, supported names, and names that equal the recorded scope
    /// ignoring only ASCII case (the supported names are ASCII, so ordinal-ignore-case is exactly that).</summary>
    private static bool IsAcceptedIdentity(GitHubRepositoryIdentity? identity, GitHubObservationTargetView target) =>
        identity is not null
        && GitHubRepositoryNodeIdPolicy.IsNodeId(identity.NodeId)
        && GitHubRepositoryNamePolicy.IsOwner(identity.Owner)
        && GitHubRepositoryNamePolicy.IsRepository(identity.Repository)
        && string.Equals(identity.Owner, target.Owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(identity.Repository, target.Repository, StringComparison.OrdinalIgnoreCase);

    private sealed record Scope(bool IsRecorded, GitHubObservationSource? Source, GitHubObservationTargetView? Target);
}
