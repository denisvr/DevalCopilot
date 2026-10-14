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

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;

/// <summary>
/// Inspects, on explicit request and in two fixed phases, the provider-supplied output of one GitHub check run associated with the exact
/// recorded commit of a run whose repository identity is bound (ADR-0047). It reads the recorded source, target and immutable binding
/// through the unchanged readers (an orphan binding or event is never "not recorded") and requires a current, successful,
/// direct-executable GitHubCli snapshot before the metadata phase (never discovering, refreshing or installing anything). The metadata
/// phase proves the bound repository identity, the exact recorded branch and commit and the requested check run's association with that
/// repository and commit; only then, after the snapshot is shown to be still usable with the same resolved path and the clock has not
/// run backwards, does the output phase run, repeating every association value, which must equal the first admitted identity. The whole
/// scope is read again after each external call and compared by complete value; any drift strips every fact and takes precedence. Every
/// whole answer is validated independently before any classification, including output that will be withheld. It writes no row, event or
/// lock, opens no transaction, persists nothing and caches nothing between requests; faults and cancellation propagate.
/// </summary>
public sealed class GetGitHubCheckRunOutputObservationQueryHandler(
    IDevalCopilotDbContext dbContext, IGitHubCheckRunOutputObserver observer, TimeProvider timeProvider)
    : IQueryHandler<GetGitHubCheckRunOutputObservationQuery, Result<GetGitHubCheckRunOutputObservationQueryResult>>
{
    public async Task<Result<GetGitHubCheckRunOutputObservationQueryResult>> HandleAsync(
        GetGitHubCheckRunOutputObservationQuery query, CancellationToken cancellationToken)
    {
        if (!GitHubCheckRunOutputPolicy.IsCheckRunId(query.CheckRunId))
        {
            return Result<GetGitHubCheckRunOutputObservationQueryResult>.Failure(GitHubCheckRunOutputObservationErrors.InvalidRequest());
        }

        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == query.RunId, cancellationToken))
        {
            return Result<GetGitHubCheckRunOutputObservationQueryResult>.Failure(GitHubCheckRunOutputObservationErrors.NotFound());
        }

        var scope = await ReadScopeAsync(query.RunId, cancellationToken);
        if (scope is not { Kind: ScopeKind.Bound, Source: { } source, Target: { } target, Binding: { } binding })
        {
            return scope.Kind switch
            {
                ScopeKind.NotRecorded => Reply(query, GitHubCheckRunOutputObservationStatus.NotRecorded, null, null, null, null),
                ScopeKind.BindingNotRecorded => Reply(
                    query, GitHubCheckRunOutputObservationStatus.BindingNotRecorded, scope.Source?.ToView(), scope.Target, null, null),
                _ => Reply(query, GitHubCheckRunOutputObservationStatus.Unavailable, null, null, null, null),
            };
        }

        var sourceView = source.ToView();
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        if (!IsUsableTarget(snapshot, timeProvider.GetUtcNow()))
        {
            return Reply(query, GitHubCheckRunOutputObservationStatus.CliUnavailable, sourceView, target, binding, null);
        }

        var executablePath = snapshot!.ResolvedExecutablePath!;
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = await observer.ObserveMetadataAsync(
            executablePath, target.Owner, target.Repository, source.BranchName, query.CheckRunId, cancellationToken);

        if (await ReadScopeAsync(query.RunId, cancellationToken) != scope)
        {
            return Reply(query, GitHubCheckRunOutputObservationStatus.Unavailable, null, null, null, null);
        }

        var first = Evaluate(metadata, query.CheckRunId, requireDetail: false, null, target, source, binding);
        if (first.Outcome != Outcome.Admitted)
        {
            return Conclude(query, first, metadata, sourceView, target, binding);
        }

        var between = await ReadSnapshotAsync(cancellationToken);
        if (!IsUsableTarget(between, timeProvider.GetUtcNow())
            || !string.Equals(between!.ResolvedExecutablePath, executablePath, StringComparison.Ordinal))
        {
            return Reply(query, GitHubCheckRunOutputObservationStatus.CliUnavailable, sourceView, target, binding, null);
        }

        if (timeProvider.GetUtcNow() < binding.RecordedAtUtc)
        {
            return Reply(query, GitHubCheckRunOutputObservationStatus.Unknown, sourceView, target, binding, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var output = await observer.ObserveOutputAsync(
            executablePath, target.Owner, target.Repository, source.BranchName, query.CheckRunId, cancellationToken);

        if (await ReadScopeAsync(query.RunId, cancellationToken) != scope)
        {
            return Reply(query, GitHubCheckRunOutputObservationStatus.Unavailable, null, null, null, null);
        }

        var second = Evaluate(output, query.CheckRunId, requireDetail: true, first.Association, target, source, binding);

        return Conclude(query, second, output, sourceView, target, binding);
    }

    private Result<GetGitHubCheckRunOutputObservationQueryResult> Conclude(
        GetGitHubCheckRunOutputObservationQuery query,
        Evaluation evaluation,
        GitHubCheckRunProbe? probe,
        GitHubObservationSourceView sourceView,
        GitHubObservationTargetView target,
        GitHubRepositoryIdentityBindingView binding)
    {
        if (evaluation.Outcome == Outcome.Invalid)
        {
            return Reply(query, GitHubCheckRunOutputObservationStatus.Unknown, sourceView, target, binding, null);
        }

        if (evaluation.Outcome == Outcome.IdentityChanged)
        {
            return Reply(query, GitHubCheckRunOutputObservationStatus.IdentityChanged, sourceView, target, binding, null);
        }

        var observedAt = timeProvider.GetUtcNow();
        if (observedAt < binding.RecordedAtUtc)
        {
            return Reply(query, GitHubCheckRunOutputObservationStatus.Unknown, sourceView, target, binding, null);
        }

        (GitHubCheckRunOutputObservationStatus Status, GitHubCheckRunOutputCheckRun? CheckRun) classified = evaluation.Outcome switch
        {
            Outcome.RefNotReturned => (GitHubCheckRunOutputObservationStatus.RefNotReturned, null),
            Outcome.CommitDifferent => (GitHubCheckRunOutputObservationStatus.CommitDifferent, null),
            Outcome.CheckRunUnavailable => (GitHubCheckRunOutputObservationStatus.CheckRunUnavailable, null),
            _ => AdmittedCheckRun(probe!.Node!, evaluation.Association!),
        };
        var observed = new GitHubCheckRunOutputObserved(
            probe!.NodeId, probe.Owner, probe.Repository, observedAt, probe.Ref, classified.CheckRun);

        return Reply(query, classified.Status, sourceView, target, binding, observed);
    }

    private static (GitHubCheckRunOutputObservationStatus Status, GitHubCheckRunOutputCheckRun? CheckRun) AdmittedCheckRun(
        GitHubCheckRunNode node, GitHubCheckRunAssociation association)
    {
        var detail = node.Detail!;
        var output = detail.Output is { Title: null, Summary: null, Text: null } ? null : detail.Output;
        var checkRun = new GitHubCheckRunOutputCheckRun(
            association.CheckRunId, association.CheckSuiteId, association.CommitSha, detail.Name, detail.Status, detail.Conclusion, output);

        return (
            output is null ? GitHubCheckRunOutputObservationStatus.OutputNotReturned : GitHubCheckRunOutputObservationStatus.OutputReported,
            checkRun);
    }

    private static Result<GetGitHubCheckRunOutputObservationQueryResult> Reply(
        GetGitHubCheckRunOutputObservationQuery query,
        GitHubCheckRunOutputObservationStatus status,
        GitHubObservationSourceView? source,
        GitHubObservationTargetView? target,
        GitHubRepositoryIdentityBindingView? binding,
        GitHubCheckRunOutputObserved? observation) =>
        Result<GetGitHubCheckRunOutputObservationQueryResult>.Success(
            new GetGitHubCheckRunOutputObservationQueryResult(query.RunId, query.CheckRunId, status, source, target, binding, observation));

    private Task<HostCapabilitySnapshot?> ReadSnapshotAsync(CancellationToken cancellationToken) =>
        dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.GitHubCli, cancellationToken);

    /// <summary>The one coherent reading of the recorded scope, taken before the metadata call and after each external call and compared
    /// whole. A coherent target without a binding row or event is genuinely not bound; a missing target with nothing recorded is not
    /// recorded; anything else, including a binding or event without a coherent target, is unavailable.</summary>
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

    /// <summary>Validates the whole answer first (nothing is partially admitted), then classifies it in a fixed order: the root
    /// repository identity, then the ref, then the check association. <paramref name="first"/> is the association admitted by the
    /// metadata phase; the output phase must repeat it exactly, otherwise the association changed and the check is unavailable.</summary>
    private static Evaluation Evaluate(
        GitHubCheckRunProbe? probe,
        string checkRunId,
        bool requireDetail,
        GitHubCheckRunAssociation? first,
        GitHubObservationTargetView target,
        GitHubObservationSource source,
        GitHubRepositoryIdentityBindingView binding)
    {
        if (!IsAcceptedProbe(probe, checkRunId, requireDetail, target, source))
        {
            return new Evaluation(Outcome.Invalid, null);
        }

        if (!string.Equals(probe!.NodeId, binding.RepositoryNodeId, StringComparison.Ordinal))
        {
            return new Evaluation(Outcome.IdentityChanged, null);
        }

        if (probe.Ref is null)
        {
            return new Evaluation(Outcome.RefNotReturned, null);
        }

        if (!string.Equals(probe.Ref.CommitSha, source.CommitSha, StringComparison.Ordinal))
        {
            return new Evaluation(Outcome.CommitDifferent, null);
        }

        var association = probe.Node?.Association;
        var belongs = association is not null
            && string.Equals(association.CheckRunRepositoryId, binding.RepositoryNodeId, StringComparison.Ordinal)
            && string.Equals(association.CheckSuiteRepositoryId, binding.RepositoryNodeId, StringComparison.Ordinal)
            && string.Equals(association.CommitSha, source.CommitSha, StringComparison.Ordinal)
            && (first is null || association == first);

        return belongs ? new Evaluation(Outcome.Admitted, association) : new Evaluation(Outcome.CheckRunUnavailable, null);
    }

    /// <summary>The whole port result is validated before anything is compared: a conservative opaque ID, supported names equal to the
    /// recorded scope ignoring only ASCII case, a ref that is absent or has exactly the recorded owned branch name and a well-formed
    /// commit, and a node that is absent, a coherent check run echoing the requested identifier (with its detail exactly in the output
    /// phase) or another type with nothing else, whatever it will later be withheld for.</summary>
    private static bool IsAcceptedProbe(
        GitHubCheckRunProbe? probe,
        string checkRunId,
        bool requireDetail,
        GitHubObservationTargetView target,
        GitHubObservationSource source) =>
        probe is not null
        && GitHubRepositoryNodeIdPolicy.IsNodeId(probe.NodeId)
        && GitHubRepositoryNamePolicy.IsOwner(probe.Owner)
        && GitHubRepositoryNamePolicy.IsRepository(probe.Repository)
        && string.Equals(probe.Owner, target.Owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(probe.Repository, target.Repository, StringComparison.OrdinalIgnoreCase)
        && (probe.Ref is null
            || (GitHubOwnedBranchNamePolicy.IsOwnedBranch(probe.Ref.BranchName)
                && string.Equals(probe.Ref.BranchName, source.BranchName, StringComparison.Ordinal)
                && GitHubObservationTarget.IsCommitSha(probe.Ref.CommitSha)))
        && GitHubCheckRunOutputPolicy.IsNode(probe.Node, checkRunId, requireDetail);

    private enum ScopeKind
    {
        NotRecorded,
        BindingNotRecorded,
        Bound,
        Unavailable,
    }

    private enum Outcome
    {
        Invalid,
        IdentityChanged,
        RefNotReturned,
        CommitDifferent,
        CheckRunUnavailable,
        Admitted,
    }

    private sealed record Evaluation(Outcome Outcome, GitHubCheckRunAssociation? Association);

    private sealed record Scope(
        ScopeKind Kind,
        GitHubObservationSource? Source,
        GitHubObservationTargetView? Target,
        GitHubRepositoryIdentityBindingView? Binding);
}
