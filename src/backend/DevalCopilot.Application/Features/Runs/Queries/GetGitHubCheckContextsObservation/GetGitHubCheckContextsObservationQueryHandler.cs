using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;

/// <summary>
/// Observes the bounded check and status contexts GitHub returns for the exact recorded commit of a run whose repository identity is bound,
/// on explicit request (ADR-0045). It reads the recorded source, target and immutable binding through the unchanged readers (an orphan
/// binding or event is never "not recorded"), invokes the observer only for a coherent bound scope and a current, successful,
/// direct-executable GitHubCli snapshot (never discovering, refreshing or installing anything), reads the whole scope and binding again
/// after the external work and refuses the answer when anything was lost, changed or became incoherent, and independently validates
/// everything the port returned before any comparison: the repository identity, an exact branch ref, a well-formed commit and the whole
/// bounded page. Only then is the opaque ID compared with the binding and the commit with the recorded commit, ordinally; a count and the
/// entries are exposed only for the recorded commit and only within the 50-entry bound. It writes no row, event or lock and holds no
/// transaction across the external call; faults and cancellation propagate.
/// </summary>
public sealed class GetGitHubCheckContextsObservationQueryHandler(
    IDevalCopilotDbContext dbContext, IGitHubCheckContextsObserver observer, TimeProvider timeProvider)
    : IQueryHandler<GetGitHubCheckContextsObservationQuery, Result<GetGitHubCheckContextsObservationQueryResult>>
{
    public async Task<Result<GetGitHubCheckContextsObservationQueryResult>> HandleAsync(
        GetGitHubCheckContextsObservationQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == query.RunId, cancellationToken))
        {
            return Result<GetGitHubCheckContextsObservationQueryResult>.Failure(GitHubCheckContextsObservationErrors.NotFound());
        }

        var scope = await ReadScopeAsync(query.RunId, cancellationToken);
        if (scope is not { Kind: ScopeKind.Bound, Source: { } source, Target: { } target, Binding: { } binding })
        {
            return scope.Kind switch
            {
                ScopeKind.NotRecorded => Reply(query.RunId, GitHubCheckContextsObservationStatus.NotRecorded, null, null, null, null),
                ScopeKind.BindingNotRecorded => Reply(
                    query.RunId, GitHubCheckContextsObservationStatus.BindingNotRecorded, scope.Source?.ToView(), scope.Target, null, null),
                _ => Reply(query.RunId, GitHubCheckContextsObservationStatus.Unavailable, null, null, null, null),
            };
        }

        var sourceView = source.ToView();
        var snapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.GitHubCli, cancellationToken);
        if (!IsUsableTarget(snapshot, timeProvider.GetUtcNow()))
        {
            return Reply(query.RunId, GitHubCheckContextsObservationStatus.CliUnavailable, sourceView, target, binding, null);
        }

        var probe = await observer.ObserveAsync(
            snapshot!.ResolvedExecutablePath!, target.Owner, target.Repository, source.BranchName, cancellationToken);

        var after = await ReadScopeAsync(query.RunId, cancellationToken);
        if (after != scope)
        {
            return Reply(query.RunId, GitHubCheckContextsObservationStatus.Unavailable, null, null, null, null);
        }

        if (!IsAcceptedProbe(probe, target, source))
        {
            return Reply(query.RunId, GitHubCheckContextsObservationStatus.Unknown, sourceView, target, binding, null);
        }

        if (!string.Equals(probe!.NodeId, binding.RepositoryNodeId, StringComparison.Ordinal))
        {
            return Reply(query.RunId, GitHubCheckContextsObservationStatus.IdentityChanged, sourceView, target, binding, null);
        }

        var observedAt = timeProvider.GetUtcNow();
        if (observedAt < binding.RecordedAtUtc)
        {
            return Reply(query.RunId, GitHubCheckContextsObservationStatus.Unknown, sourceView, target, binding, null);
        }

        var status = probe.Ref is null
            ? GitHubCheckContextsObservationStatus.RefNotReturned
            : !string.Equals(probe.Ref.CommitSha, source.CommitSha, StringComparison.Ordinal)
                ? GitHubCheckContextsObservationStatus.CommitDifferent
                : probe.Page is null
                    ? GitHubCheckContextsObservationStatus.NoContextsReturned
                    : probe.Page.TotalCount > GitHubCheckContextsPolicy.MaxContexts
                        ? GitHubCheckContextsObservationStatus.LimitExceeded
                        : GitHubCheckContextsObservationStatus.ContextsReported;
        var observed = new GitHubCheckContextsObserved(
            probe.NodeId,
            probe.Owner,
            probe.Repository,
            observedAt,
            probe.Ref,
            status is GitHubCheckContextsObservationStatus.LimitExceeded or GitHubCheckContextsObservationStatus.ContextsReported
                ? probe.Page!.TotalCount
                : null,
            status == GitHubCheckContextsObservationStatus.ContextsReported ? probe.Page!.Contexts : null);

        return Reply(query.RunId, status, sourceView, target, binding, observed);
    }

    private static Result<GetGitHubCheckContextsObservationQueryResult> Reply(
        Guid runId,
        GitHubCheckContextsObservationStatus status,
        GitHubObservationSourceView? source,
        GitHubObservationTargetView? target,
        GitHubRepositoryIdentityBindingView? binding,
        GitHubCheckContextsObserved? observation) =>
        Result<GetGitHubCheckContextsObservationQueryResult>.Success(
            new GetGitHubCheckContextsObservationQueryResult(runId, status, source, target, binding, observation));

    /// <summary>The one coherent reading of the recorded scope, taken before and after the external call and compared whole. A coherent
    /// target without a binding row or event is genuinely not bound; a missing target with nothing recorded is not recorded; anything else,
    /// including a binding or event without a coherent target, is unavailable.</summary>
    private async Task<Scope> ReadScopeAsync(Guid runId, CancellationToken cancellationToken)
    {
        var source = await GitHubObservationSourceReader.ReadAsync(dbContext, runId, cancellationToken);
        var targetReading = await GitHubObservationTargetReader.ReadAsync(dbContext, runId, source, cancellationToken);
        var target = targetReading.Kind == GitHubObservationTargetReadingKind.Coherent && source is not null ? targetReading.Target : null;
        var bindingReading = await GitHubRepositoryIdentityBindingReader.ReadAsync(dbContext, runId, source, target, cancellationToken);

        return (targetReading.Kind, bindingReading.Kind) switch
        {
            (GitHubObservationTargetReadingKind.None, GitHubRepositoryIdentityBindingReadingKind.None) =>
                new Scope(ScopeKind.NotRecorded, null, null, null),
            (GitHubObservationTargetReadingKind.Coherent, GitHubRepositoryIdentityBindingReadingKind.None) when source is not null && target is not null =>
                new Scope(ScopeKind.BindingNotRecorded, source, target, null),
            (GitHubObservationTargetReadingKind.Coherent, GitHubRepositoryIdentityBindingReadingKind.Coherent)
                when source is not null && target is not null && bindingReading.Binding is not null =>
                new Scope(ScopeKind.Bound, source, target, bindingReading.Binding),
            _ => new Scope(ScopeKind.Unavailable, null, null, null),
        };
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

    /// <summary>The whole port result is validated before anything is compared: a conservative opaque ID, supported names equal to the
    /// recorded scope ignoring only ASCII case (the supported names are ASCII, so ordinal-ignore-case is exactly that), a ref that is
    /// absent or has exactly the recorded owned branch name and a well-formed commit, and a page that exists only beside a ref and is
    /// internally consistent whatever it will later be withheld for. A malformed answer is never partially admitted as an identity change,
    /// a commit difference or an excessive count.</summary>
    private static bool IsAcceptedProbe(GitHubCheckContextsProbe? probe, GitHubObservationTargetView target, GitHubObservationSource source) =>
        probe is not null
        && GitHubRepositoryNodeIdPolicy.IsNodeId(probe.NodeId)
        && GitHubRepositoryNamePolicy.IsOwner(probe.Owner)
        && GitHubRepositoryNamePolicy.IsRepository(probe.Repository)
        && string.Equals(probe.Owner, target.Owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(probe.Repository, target.Repository, StringComparison.OrdinalIgnoreCase)
        && (probe.Ref is null
            ? probe.Page is null
            : GitHubOwnedBranchNamePolicy.IsOwnedBranch(probe.Ref.BranchName)
                && string.Equals(probe.Ref.BranchName, source.BranchName, StringComparison.Ordinal)
                && GitHubObservationTarget.IsCommitSha(probe.Ref.CommitSha)
                && (probe.Page is null || GitHubCheckContextsPolicy.IsPage(probe.Page)));

    private enum ScopeKind
    {
        NotRecorded,
        BindingNotRecorded,
        Bound,
        Unavailable,
    }

    private sealed record Scope(
        ScopeKind Kind,
        GitHubObservationSource? Source,
        GitHubObservationTargetView? Target,
        GitHubRepositoryIdentityBindingView? Binding);
}
