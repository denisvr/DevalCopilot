using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.BindGitHubRepositoryIdentity;

/// <summary>
/// Records one immutable GitHub repository identity binding and exactly one Human-authored event, atomically (ADR-0043). The scope comes
/// only from the unchanged ADR-0040 readers over pinned rows. For a missing binding it requires the same current, successful,
/// direct-executable GitHub CLI snapshot as ADR-0041 and invokes the existing identity observer exactly once with no transaction and no
/// write lock held, then independently validates the observed node identifier and names. Only afterwards does it enter one short
/// transaction whose first statement is the no-op Run write that takes the database write lock, freshly rereads the whole scope and the
/// binding, requires the scope to be exactly the one observed, and inserts the binding and its event together. A coherent same-identifier
/// binding replays its original record, event and times without another observation or insert; a different one conflicts; an
/// incoherent record never replays. The run, lifecycle, stage, clocks, workspace, lease, approvals and budgets are never written. A refusal
/// records nothing; a cancelled or failed save rolls back both inserts; faults propagate.
/// </summary>
public sealed class BindGitHubRepositoryIdentityCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitHubRepositoryIdentityObserver observer,
    TimeProvider timeProvider,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<BindGitHubRepositoryIdentityCommand, Result<BindGitHubRepositoryIdentityCommandResult>>
{
    public async Task<Result<BindGitHubRepositoryIdentityCommandResult>> HandleAsync(
        BindGitHubRepositoryIdentityCommand command, CancellationToken cancellationToken)
    {
        if (command.RunId == Guid.Empty || command.OperationId == Guid.Empty || command.TargetId == Guid.Empty
            || !GitHubObservationTarget.IsCommitSha(command.CommitSha) || !GitHubRepositoryNodeIdPolicy.IsNodeId(command.RepositoryNodeId))
        {
            return Fail(GitHubRepositoryIdentityBindingErrors.Invalid());
        }

        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == command.RunId, cancellationToken))
        {
            return Fail(GitHubRepositoryIdentityBindingErrors.NotFound());
        }

        var before = await ReadScopeAsync(command, cancellationToken);
        if (before.Scope is null || before.Binding.Kind == GitHubRepositoryIdentityBindingReadingKind.Incoherent)
        {
            return Fail(GitHubRepositoryIdentityBindingErrors.Unavailable());
        }

        GitHubRepositoryIdentity? observedIdentity = null;
        if (before.Binding.Kind == GitHubRepositoryIdentityBindingReadingKind.None)
        {
            var (refusal, identity) = await ObserveAsync(command, before.Scope, cancellationToken);
            if (refusal is not null)
            {
                return Fail(refusal);
            }

            observedIdentity = identity;
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The transaction's first statement: a no-op write that takes the database write lock. Everything below is read after it, so it is
        // atomic with the inserts saved at the end. Zero rows means the run no longer exists.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == command.RunId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Fail(GitHubRepositoryIdentityBindingErrors.NotFound());
        }

        var after = await ReadScopeAsync(command, cancellationToken);
        if (after.Scope is null || after.Scope != before.Scope)
        {
            return Fail(GitHubRepositoryIdentityBindingErrors.Unavailable());
        }

        switch (after.Binding)
        {
            case { Kind: GitHubRepositoryIdentityBindingReadingKind.Incoherent }:
                return Fail(GitHubRepositoryIdentityBindingErrors.Unavailable());
            case { Kind: GitHubRepositoryIdentityBindingReadingKind.Coherent, Binding: { } recorded }:
                return string.Equals(recorded.RepositoryNodeId, command.RepositoryNodeId, StringComparison.Ordinal)
                    ? Succeed(after.Scope, recorded)
                    : Fail(GitHubRepositoryIdentityBindingErrors.Conflict());
        }

        // No binding exists. Without an observation made in this request (a replay raced with a vanished record) nothing is recorded.
        if (observedIdentity is null)
        {
            return Fail(GitHubRepositoryIdentityBindingErrors.Unavailable());
        }

        var scope = after.Scope;
        var observedAtUtc = timeProvider.GetUtcNow();
        if (observedAtUtc < scope.Target.RecordedAtUtc)
        {
            return Fail(GitHubRepositoryIdentityBindingErrors.Unavailable());
        }

        var recordedAtUtc = timeProvider.GetUtcNow();
        if (recordedAtUtc < observedAtUtc)
        {
            // A clock that went backwards between the two readings is never papered over, retried or reobserved.
            return Fail(GitHubRepositoryIdentityBindingErrors.Unavailable());
        }

        var binding = GitHubRepositoryIdentityBinding.Record(
            Guid.NewGuid(), scope.Source.RunId, scope.Source.ProjectId, scope.Source.OperationId, scope.Source.WorkspaceId, scope.Target.Id,
            scope.Source.CommitSha, scope.Source.BranchName, command.RepositoryNodeId, observedIdentity.Owner, observedIdentity.Repository,
            scope.Target.RecordedAtUtc, observedAtUtc, recordedAtUtc, Guid.NewGuid());
        var recordedEvent = RunEvent.Record(
            binding.EventId,
            scope.Source.RunId,
            attemptId: null,
            RunEventType.GitHubRepositoryIdentityBound,
            ParticipantIdentity.ForHuman(),
            GitHubRepositoryIdentityBindingEventPayload.Build(binding),
            recordedAtUtc);
        dbContext.GitHubRepositoryIdentityBindings.Add(binding);
        dbContext.Events.Add(recordedEvent);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await NotifyAsync(scope.Source.RunId, recordedEvent.Sequence, cancellationToken);

        return Succeed(
            scope,
            new GitHubRepositoryIdentityBindingView(
                binding.Id, binding.EventId, binding.RepositoryNodeId, binding.Owner, binding.Repository, binding.ObservedAtUtc,
                binding.RecordedAtUtc));
    }

    private static Result<BindGitHubRepositoryIdentityCommandResult> Fail(Error error) =>
        Result<BindGitHubRepositoryIdentityCommandResult>.Failure(error);

    private static Result<BindGitHubRepositoryIdentityCommandResult> Succeed(
        BoundScope scope, GitHubRepositoryIdentityBindingView binding) =>
        Result<BindGitHubRepositoryIdentityCommandResult>.Success(
            new BindGitHubRepositoryIdentityCommandResult(scope.Source.RunId, scope.Source.ToView(), scope.Target, binding));

    /// <summary>The exact recorded scope, or no scope. The submitted operation, commit and target must equal it; a binding or event that is
    /// not coherent with it is reported as an incoherent binding, never an absent one.</summary>
    private async Task<ScopeReading> ReadScopeAsync(BindGitHubRepositoryIdentityCommand command, CancellationToken cancellationToken)
    {
        var source = await GitHubObservationSourceReader.ReadAsync(dbContext, command.RunId, cancellationToken);
        var targetReading = await GitHubObservationTargetReader.ReadAsync(dbContext, command.RunId, source, cancellationToken);
        var target = targetReading.Kind == GitHubObservationTargetReadingKind.Coherent ? targetReading.Target : null;
        var binding = await GitHubRepositoryIdentityBindingReader.ReadAsync(dbContext, command.RunId, source, target, cancellationToken);

        return source is not null && target is not null && source.OperationId == command.OperationId
            && string.Equals(source.CommitSha, command.CommitSha, StringComparison.Ordinal) && target.Id == command.TargetId
                ? new ScopeReading(new BoundScope(source, target), binding)
                : new ScopeReading(null, binding);
    }

    /// <summary>One observation, with nothing locked and no transaction open: the CLI snapshot must be usable, the observer must answer, and
    /// its answer is validated again here. No refusal means the identity was observed and agrees with the request, and the validated
    /// identity is returned so exactly the names the observer reported are the ones recorded.</summary>
    private async Task<(Error? Refusal, GitHubRepositoryIdentity? Identity)> ObserveAsync(
        BindGitHubRepositoryIdentityCommand command, BoundScope scope, CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.GitHubCli, cancellationToken);
        if (!IsUsableSnapshot(snapshot, timeProvider.GetUtcNow()))
        {
            return (GitHubRepositoryIdentityBindingErrors.CliUnavailable(), null);
        }

        var identity = await observer.ObserveAsync(
            snapshot!.ResolvedExecutablePath!, scope.Target.Owner, scope.Target.Repository, cancellationToken);
        if (!IsAcceptedIdentity(identity, scope.Target))
        {
            return (GitHubRepositoryIdentityBindingErrors.ObservationUnknown(), null);
        }

        return string.Equals(identity!.NodeId, command.RepositoryNodeId, StringComparison.Ordinal)
            ? (null, identity)
            : (GitHubRepositoryIdentityBindingErrors.IdentityChanged(), null);
    }

    private static bool IsUsableSnapshot(HostCapabilitySnapshot? snapshot, DateTimeOffset nowUtc) =>
        snapshot is not null
        && snapshot.ReasonCode == CapabilityProbeReason.None
        && snapshot.LaunchKind == CapabilityLaunchKind.DirectExecutable
        && snapshot.ResolvedScriptPath is null
        && !string.IsNullOrWhiteSpace(snapshot.ResolvedExecutablePath)
        && Path.IsPathFullyQualified(snapshot.ResolvedExecutablePath)
        && !string.IsNullOrWhiteSpace(snapshot.ObservedVersion)
        && snapshot.EvidenceObservedAtUtc.HasValue
        && !HostCapabilityReadinessProjector.IsStale(snapshot, nowUtc);

    /// <summary>The port is validated again here: a conservative opaque ID, supported names, and names that equal the recorded target
    /// ignoring only ASCII case (the supported names are ASCII, so ordinal-ignore-case is exactly that).</summary>
    private static bool IsAcceptedIdentity(GitHubRepositoryIdentity? identity, GitHubObservationTargetView target) =>
        identity is not null
        && GitHubRepositoryNodeIdPolicy.IsNodeId(identity.NodeId)
        && GitHubRepositoryNamePolicy.IsOwner(identity.Owner)
        && GitHubRepositoryNamePolicy.IsRepository(identity.Repository)
        && string.Equals(identity.Owner, target.Owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(identity.Repository, target.Repository, StringComparison.OrdinalIgnoreCase);

    /// <summary>Run-advance notifications are only hints (ADR-0003): a notifier failure after the commit never turns the recorded
    /// binding into a reported failure. Cancellation still propagates.</summary>
    private async Task NotifyAsync(Guid runId, long sequence, CancellationToken cancellationToken)
    {
        if (eventNotifier is null)
        {
            return;
        }

        try
        {
            await eventNotifier.NotifyRunAdvancedAsync(runId, sequence, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The event is durable; a later cursor read delivers it.
        }
    }

    private sealed record BoundScope(GitHubObservationSource Source, GitHubObservationTargetView Target);

    private sealed record ScopeReading(BoundScope? Scope, GitHubRepositoryIdentityBindingReading Binding);
}
