using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;

/// <summary>
/// Observes the repository permission GitHub reports for the stored default authentication on the repository of a run whose identity is
/// bound, on explicit request (ADR-0049). It reads the recorded source, target and immutable binding through the unchanged readers (an
/// orphan binding or event is never "not recorded"), invokes the observer only for a coherent bound scope and a current, successful,
/// direct-executable GitHubCli snapshot (never discovering, refreshing or installing anything), reads the whole scope and binding again
/// after the external work and refuses the answer when anything was lost, changed or became incoherent, and independently validates
/// everything the port returned before any comparison. Only then is the opaque ID compared with the binding, ordinally. The reported
/// permission is a fact of that moment, never a derived capability: nothing is ranked, translated or turned into an allowance. It writes
/// no row, event or lock and holds no transaction across the external call; faults and cancellation propagate.
/// </summary>
public sealed class GetGitHubRepositoryPermissionObservationQueryHandler(
    IDevalCopilotDbContext dbContext, IGitHubRepositoryPermissionObserver observer, TimeProvider timeProvider)
    : IQueryHandler<GetGitHubRepositoryPermissionObservationQuery, Result<GetGitHubRepositoryPermissionObservationQueryResult>>
{
    public async Task<Result<GetGitHubRepositoryPermissionObservationQueryResult>> HandleAsync(
        GetGitHubRepositoryPermissionObservationQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == query.RunId, cancellationToken))
        {
            return Result<GetGitHubRepositoryPermissionObservationQueryResult>.Failure(
                GitHubRepositoryPermissionObservationErrors.NotFound());
        }

        var scope = await ReadScopeAsync(query.RunId, cancellationToken);
        if (scope is not { Kind: ScopeKind.Bound, Source: { } source, Target: { } target, Binding: { } binding })
        {
            return scope.Kind switch
            {
                ScopeKind.NotRecorded => Reply(query.RunId, GitHubRepositoryPermissionObservationStatus.NotRecorded, null, null, null, null),
                ScopeKind.BindingNotRecorded => Reply(
                    query.RunId,
                    GitHubRepositoryPermissionObservationStatus.BindingNotRecorded,
                    scope.Source?.ToView(),
                    scope.Target,
                    null,
                    null),
                _ => Reply(query.RunId, GitHubRepositoryPermissionObservationStatus.Unavailable, null, null, null, null),
            };
        }

        var sourceView = source.ToView();
        var snapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.GitHubCli, cancellationToken);
        if (!IsUsableTarget(snapshot, timeProvider.GetUtcNow()))
        {
            return Reply(query.RunId, GitHubRepositoryPermissionObservationStatus.CliUnavailable, sourceView, target, binding, null);
        }

        var probe = await observer.ObserveAsync(snapshot!.ResolvedExecutablePath!, target.Owner, target.Repository, cancellationToken);

        var after = await ReadScopeAsync(query.RunId, cancellationToken);
        if (after != scope)
        {
            return Reply(query.RunId, GitHubRepositoryPermissionObservationStatus.Unavailable, null, null, null, null);
        }

        if (!GitHubRepositoryPermissionPolicy.IsAcceptedProbe(probe, target))
        {
            return Reply(query.RunId, GitHubRepositoryPermissionObservationStatus.Unknown, sourceView, target, binding, null);
        }

        if (!string.Equals(probe!.NodeId, binding.RepositoryNodeId, StringComparison.Ordinal))
        {
            return Reply(query.RunId, GitHubRepositoryPermissionObservationStatus.IdentityChanged, sourceView, target, binding, null);
        }

        var observedAt = timeProvider.GetUtcNow();
        if (observedAt < binding.RecordedAtUtc)
        {
            return Reply(query.RunId, GitHubRepositoryPermissionObservationStatus.Unknown, sourceView, target, binding, null);
        }

        var status = probe.ViewerPermission is null
            ? GitHubRepositoryPermissionObservationStatus.PermissionNotReturned
            : GitHubRepositoryPermissionObservationStatus.PermissionReported;
        var observed = new GitHubRepositoryPermissionObserved(probe.NodeId, probe.Owner, probe.Repository, observedAt, probe.ViewerPermission);

        return Reply(query.RunId, status, sourceView, target, binding, observed);
    }

    private static Result<GetGitHubRepositoryPermissionObservationQueryResult> Reply(
        Guid runId,
        GitHubRepositoryPermissionObservationStatus status,
        GitHubObservationSourceView? source,
        GitHubObservationTargetView? target,
        GitHubRepositoryIdentityBindingView? binding,
        GitHubRepositoryPermissionObserved? observation) =>
        Result<GetGitHubRepositoryPermissionObservationQueryResult>.Success(
            new GetGitHubRepositoryPermissionObservationQueryResult(runId, status, source, target, binding, observation));

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
