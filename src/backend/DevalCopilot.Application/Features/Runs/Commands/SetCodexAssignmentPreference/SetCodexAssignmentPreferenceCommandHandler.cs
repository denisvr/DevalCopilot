using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.SetCodexAssignmentPreference;

/// <summary>
/// Validates a new non-null requested Codex model/effort pair against one fresh, bounded catalog
/// observation from the same already-vetted Codex launch target every other Codex path uses, then
/// durably records the run-scoped preference and its change event. Never auto-selects the
/// catalog's own suggested default; clearing (a null requested model) never reads the catalog.
/// This never mutates an already-claimed attempt's own immutable assignment — only a later claim
/// reads the newly recorded preference.
///
/// <para>
/// Manual transaction: the bounded external catalog observation (a real child-process round trip)
/// runs first, entirely untracked and with no database write pending — this handler never opens an
/// EF transaction for it. Only after that external call completes does this handler read the Run
/// afresh (a genuinely new tracked query, never the untracked pre-check row) and, in one short,
/// tightly scoped write — a single <c>SaveChangesAsync</c> call with no external I/O awaited
/// around it — re-validate its current lifecycle, apply the preference, append its change event,
/// and commit both together. A pre-check before the external call exists only to avoid a wasted
/// catalog round trip for an already-missing or already-terminal run; it is advisory, never
/// authoritative, since either fact could change while the external call is in flight — the
/// authoritative fresh read after the external call closes that window.
/// </para>
///
/// <para>
/// A second, narrower race remains even after that fresh read: another transaction could commit a
/// lifecycle transition between this handler's own read and its own <c>SaveChangesAsync</c>, with
/// no further I/O of this handler's own in between to re-check against. Rather than wrapping that
/// gap in an explicit multi-statement transaction, <c>Run.Lifecycle</c> is configured as an EF
/// concurrency token (see <c>RunConfiguration</c>): the UPDATE this handler's save produces
/// requires the exact Lifecycle value it read, so a concurrent transition committed in that window
/// makes the UPDATE match zero rows and throws <see cref="DbUpdateConcurrencyException"/> — caught
/// below and reported identically to the ordinary in-memory lifecycle rejection, with neither the
/// preference nor its event persisted (the failed save rolls back the whole batch).
/// </para>
/// </summary>
public sealed class SetCodexAssignmentPreferenceCommandHandler(
    IDevalCopilotDbContext dbContext, ICodexModelCatalogAdapter catalogAdapter, TimeProvider timeProvider)
    : ICommandHandler<SetCodexAssignmentPreferenceCommand, Result<SetCodexAssignmentPreferenceCommandResult>>
{
    public async Task<Result<SetCodexAssignmentPreferenceCommandResult>> HandleAsync(
        SetCodexAssignmentPreferenceCommand command, CancellationToken cancellationToken)
    {
        var precheckRun = await dbContext.Runs
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (precheckRun is null)
        {
            return Result<SetCodexAssignmentPreferenceCommandResult>.Failure(CodexAssignmentPreferenceErrors.RunNotFound());
        }

        if (precheckRun.Lifecycle is not (RunLifecycle.Created or RunLifecycle.Running))
        {
            return Result<SetCodexAssignmentPreferenceCommandResult>.Failure(CodexAssignmentPreferenceErrors.RunNotEditable());
        }

        // Deliberately outside any EF transaction and against no tracked entity: a bounded
        // external Codex App Server process invocation must never hold a database transaction —
        // or even an open, uncommitted change to a tracked Run — for its duration.
        if (command.RequestedModel is not null)
        {
            var validation = await ValidateAgainstFreshCatalogAsync(command.RequestedModel, command.RequestedEffort, cancellationToken);
            if (validation is not null)
            {
                return Result<SetCodexAssignmentPreferenceCommandResult>.Failure(validation);
            }
        }

        // The short, tightly scoped write: a genuinely fresh tracked read (the pre-check above was
        // AsNoTracking, so this is a new query, never a cached identity-mapped instance), re-
        // validated for the same terminal-lifecycle race the pre-check cannot close by itself, then
        // one single SaveChangesAsync committing the preference and its event together.
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<SetCodexAssignmentPreferenceCommandResult>.Failure(CodexAssignmentPreferenceErrors.RunNotFound());
        }

        try
        {
            run.SetRequestedCodexAssignment(command.RequestedModel, command.RequestedEffort);
        }
        catch (InvalidOperationException)
        {
            return Result<SetCodexAssignmentPreferenceCommandResult>.Failure(CodexAssignmentPreferenceErrors.RunNotEditable());
        }

        var nowUtc = timeProvider.GetUtcNow();
        var payload = JsonSerializer.Serialize(new { requestedModel = run.RequestedCodexModel, requestedEffort = run.RequestedCodexEffort });
        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(),
            run.Id,
            attemptId: null,
            RunEventType.CodexAssignmentPreferenceChanged,
            ParticipantIdentity.ForHuman(),
            payload,
            nowUtc));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Lifecycle is a concurrency token (see RunConfiguration): this UPDATE's WHERE clause
            // required the exact Lifecycle value this handler's fresh read observed, and zero rows
            // matched — some other transaction committed a lifecycle transition for this same run
            // between that read and this save. Nothing from this call persisted (the failed
            // SaveChangesAsync rolled back both the Run update and the queued event insert
            // together), so this is reported exactly as the ordinary in-memory lifecycle rejection
            // above, never as a partial write.
            return Result<SetCodexAssignmentPreferenceCommandResult>.Failure(CodexAssignmentPreferenceErrors.RunNotEditable());
        }

        return Result<SetCodexAssignmentPreferenceCommandResult>.Success(
            new SetCodexAssignmentPreferenceCommandResult(run.RequestedCodexModel, run.RequestedCodexEffort));
    }

    /// <summary>Reads the same durable, already-vetted Codex launch target
    /// <c>GetCodexModelCatalogQueryHandler</c> reads (a small, deliberate duplication of that
    /// five-line lookup — this operation owns its own read rather than depending on another
    /// operation's result type) and asks <see cref="ICodexModelCatalogAdapter"/> for one fresh
    /// catalog observation. Returns <see langword="null"/> when the pair is valid; otherwise the
    /// specific rejection error. Both reads here are untracked: this method never puts a tracked
    /// entity change on the context before or during the external process call.</summary>
    private async Task<Error?> ValidateAgainstFreshCatalogAsync(
        string requestedModel, string? requestedEffort, CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.CodexCli, cancellationToken);

        if (snapshot is null || snapshot.ReasonCode != CapabilityProbeReason.None || string.IsNullOrWhiteSpace(snapshot.ResolvedExecutablePath))
        {
            return CodexAssignmentPreferenceErrors.CatalogUnavailable();
        }

        var observation = await catalogAdapter.ObserveAsync(
            snapshot.ResolvedExecutablePath, snapshot.ResolvedScriptPath, cancellationToken);
        if (!observation.IsObserved)
        {
            return CodexAssignmentPreferenceErrors.CatalogUnavailable();
        }

        var model = observation.Models.SingleOrDefault(candidate => candidate.Id == requestedModel);
        if (model is null)
        {
            return CodexAssignmentPreferenceErrors.ModelNotVisible();
        }

        if (requestedEffort is not null
            && (model.SupportedReasoningEfforts is null
                || !model.SupportedReasoningEfforts.Contains(requestedEffort, StringComparer.Ordinal)))
        {
            return CodexAssignmentPreferenceErrors.EffortNotSupported();
        }

        return null;
    }
}
