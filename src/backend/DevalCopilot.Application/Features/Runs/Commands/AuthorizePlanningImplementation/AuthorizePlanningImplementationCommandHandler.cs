using System.Data.Common;
using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DevalCopilot.Application.Features.Runs.Commands.AuthorizePlanningImplementation;

/// <summary>
/// Records the one explicit human authorization of a single implementation claim for the final plan of a completed
/// second challenge-resolution round (ADR-0016). Everything is derived from durable identity: the final Proposal from
/// the persisted escalation, the lineage from the run's provider-observed messages and attempts, the context from the
/// run, workspace, lease, checkpoint, and a fresh Git fingerprint. It claims no Agent attempt, reserves no budget, seals
/// no manifest, and starts no provider.
///
/// Bounded Git capture runs first, outside any transaction. After it, a short transaction whose first statement takes
/// the database write lock makes the fresh untracked reads (context, snapshot, existing grant) and the one save of the
/// message, grant, and event a single serialized unit, so a competing commit before BEGIN is seen and no tracked entity
/// confers stale authority. Identical retries return the recorded authorization without another message or event; a
/// different rationale conflicts; a stale or consumed grant is never revived.
/// </summary>
public sealed class AuthorizePlanningImplementationCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    TimeProvider timeProvider,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<AuthorizePlanningImplementationCommand, Result<AuthorizePlanningImplementationCommandResult>>
{
    public async Task<Result<AuthorizePlanningImplementationCommandResult>> HandleAsync(
        AuthorizePlanningImplementationCommand command, CancellationToken cancellationToken)
    {
        // Validated before any read; the refusal never echoes the submitted text.
        var rationale = PlanningImplementationInstruction.Normalize(command.Rationale);
        if (rationale is null)
        {
            return Failure(PlanningImplementationAuthorizationErrors.RationaleInvalid());
        }

        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Failure(Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        var modeError = await CurrentRunExecutionMode.CheckAgentAdmittedAsync(dbContext, run.Id, cancellationToken);
        if (modeError is not null)
        {
            return Failure(modeError);
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Failure(Error.Conflict("runs.not_running", "The run is not active."));
        }

        var workspace = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null
            || workspace.Status != WorkspaceStatus.Ready
            || !await dbContext.RepositoryMutationLeases.AsNoTracking().AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return Failure(PlanningImplementationAuthorizationErrors.ContextNotCurrent());
        }

        var checkpoint = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Failure(PlanningImplementationAuthorizationErrors.ContextNotCurrent());
        }

        // The source is decided from durable identity before any Git work, so an unknown, foreign, forged, or stale
        // escalation does no external work.
        var preliminarySnapshot = await PlanningLineage.TryLoadSnapshotAsync(dbContext, run.Id, cancellationToken);
        if (preliminarySnapshot is null)
        {
            return Failure(PlanningImplementationAuthorizationErrors.SourceInvalid());
        }

        if (Decide(preliminarySnapshot, run.Id, workspace.Id, checkpoint, command.EscalationMessageId, rationale).Error is { } early)
        {
            return Failure(early);
        }

        var evidence = await evidenceReader.CaptureAsync(workspace.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success
            || !string.Equals(evidence.FingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return Failure(PlanningImplementationAuthorizationErrors.ContextNotCurrent());
        }

        IDbContextTransaction transaction;
        try
        {
            transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        }
        catch (DbException)
        {
            return Failure(Error.Failure("planning_authorizations.persistence_failed", "The authorization could not be durably recorded."));
        }

        PlanningImplementationAuthorization? existing = null;
        CollaborationMessage? message = null;
        PlanningImplementationAuthorization? authorization = null;
        RunEvent? authorizationEvent = null;
        await using (transaction)
        {
            try
            {
                var contextFailure = await PlanningImplementationAuthorizationContext.ConfirmAsync(
                    dbContext, run, workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, cancellationToken);
                if (contextFailure is not null)
                {
                    return Failure(contextFailure switch
                    {
                        PlanningImplementationAuthorizationContext.Failure.ModeNotAdmitted => CurrentRunExecutionMode.NotAdmitted(),
                        PlanningImplementationAuthorizationContext.Failure.NotRunning =>
                            Error.Conflict("runs.not_running", "The run is not active."),
                        _ => PlanningImplementationAuthorizationErrors.ContextNotCurrent(),
                    });
                }

                // Every authority fact below is read afresh and untracked inside the write-locked transaction.
                var snapshot = await PlanningLineage.TryLoadSnapshotAsync(dbContext, run.Id, cancellationToken);
                if (snapshot is null)
                {
                    return Failure(PlanningImplementationAuthorizationErrors.SourceInvalid());
                }

                var decision = Decide(snapshot, run.Id, workspace.Id, checkpoint, command.EscalationMessageId, rationale);
                if (decision.Error is { } decisionError)
                {
                    return Failure(decisionError);
                }

                existing = decision.Existing?.Grant;
                if (existing is null)
                {
                    var nowUtc = timeProvider.GetUtcNow();
                    var final = decision.Final!;
                    message = CollaborationMessage.RecordPlanningImplementationAuthorization(
                        Guid.NewGuid(),
                        run.Id,
                        decision.Escalation!.Id,
                        PlanningImplementationInstruction.BuildStructuredContentJson(rationale),
                        nowUtc);
                    authorization = PlanningImplementationAuthorization.Create(
                        Guid.NewGuid(),
                        run.Id,
                        decision.Escalation.Id,
                        final.Proposal.Id,
                        workspace.Id,
                        checkpoint.Id,
                        checkpoint.FingerprintSha256,
                        message.Id,
                        nowUtc);
                    dbContext.CollaborationMessages.Add(message);
                    dbContext.PlanningImplementationAuthorizations.Add(authorization);
                    authorizationEvent = RunEvent.Record(
                        Guid.NewGuid(), run.Id, null, RunEventType.CollaborationMessageRecorded, message.Actor,
                        JsonSerializer.Serialize(new
                        {
                            messageId = message.Id,
                            type = message.Type.ToString(),
                            provenance = message.Provenance.ToString(),
                        }), nowUtc);
                    dbContext.Events.Add(authorizationEvent);

                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
            }
            catch (Exception exception) when (exception is DbException or DbUpdateException)
            {
                // Nothing of a failed save is read back as committed: the transaction is discarded first, then a
                // fresh untracked read of what the database holds decides (a competing identical authorization is the
                // only success).
                await RollbackBestEffortAsync(transaction);
                return await ResolveAfterFailureAsync(run.Id, workspace.Id, checkpoint, command.EscalationMessageId, rationale, cancellationToken);
            }
        }

        if (existing is not null)
        {
            return await SuccessForExistingAsync(run.Id, existing, cancellationToken);
        }

        if (eventNotifier is not null)
        {
            await eventNotifier.NotifyRunAdvancedAsync(run.Id, authorizationEvent!.Sequence, cancellationToken);
        }

        return Result<AuthorizePlanningImplementationCommandResult>.Success(
            new(authorization!.Id, authorization.EscalationMessageId, authorization.FinalProposalMessageId, message!.Id,
                "Authorized", authorizationEvent!.Sequence));
    }

    private sealed record Decision(
        Error? Error,
        PlanningLineage.Node? Final,
        CollaborationMessage? Escalation,
        PlanningImplementationAuthorizationEvidence.Recorded? Existing);

    /// <summary>The whole authorization decision for one fresh snapshot: the source's coherence and currency, then the
    /// idempotency of any existing grant. A stale or consumed grant is never an idempotent success.</summary>
    private static Decision Decide(
        ImplementerExecutionReportEligibility.Snapshot snapshot,
        Guid runId,
        Guid workspaceId,
        GitCheckpoint checkpoint,
        Guid escalationMessageId,
        string rationale)
    {
        var source = PlanningImplementationAuthorizationEvidence.EvaluateSource(
            snapshot, runId, workspaceId, checkpoint.Id, checkpoint.FingerprintSha256, escalationMessageId);
        if (source.Final is not { } final || source.Escalation is not { } escalation)
        {
            return Refused(source.Failure switch
            {
                PlanningImplementationAuthorizationEvidence.SourceFailure.NotFound => PlanningImplementationAuthorizationErrors.SourceNotFound(),
                PlanningImplementationAuthorizationEvidence.SourceFailure.Stale => PlanningImplementationAuthorizationErrors.SourceStale(),
                _ => PlanningImplementationAuthorizationErrors.SourceInvalid(),
            });
        }

        if (PlanningImplementationAuthorizationEvidence.IsSuperseded(snapshot, final))
        {
            return Refused(PlanningImplementationAuthorizationErrors.SourceStale());
        }

        var grants = snapshot.PlanningAuthorizations
            .Where(candidate => candidate.EscalationMessageId == escalation.Id || candidate.FinalProposalMessageId == final.Proposal.Id)
            .ToArray();
        if (grants.Length == 0)
        {
            return new Decision(null, final, escalation, null);
        }

        if (grants.Length != 1
            || PlanningImplementationAuthorizationEvidence.ValidateRecorded(snapshot, grants[0]) is not { } recorded
            || recorded.Grant.EscalationMessageId != escalation.Id)
        {
            return Refused(PlanningImplementationAuthorizationErrors.RecordedInvalid());
        }

        if (!PlanningImplementationAuthorizationEvidence.IsBoundTo(
                recorded.Grant, workspaceId, checkpoint.Id, checkpoint.FingerprintSha256))
        {
            return Refused(PlanningImplementationAuthorizationErrors.SourceStale());
        }

        if (!string.Equals(recorded.Rationale, rationale, StringComparison.Ordinal))
        {
            return Refused(PlanningImplementationAuthorizationErrors.RationaleConflict());
        }

        return recorded.Grant.IsAvailable
            ? new Decision(null, final, escalation, recorded)
            : Refused(PlanningImplementationAuthorizationErrors.AlreadyConsumed());
    }

    private static Decision Refused(Error error) => new(error, null, null, null);

    private async Task<Result<AuthorizePlanningImplementationCommandResult>> ResolveAfterFailureAsync(
        Guid runId,
        Guid workspaceId,
        GitCheckpoint checkpoint,
        Guid escalationMessageId,
        string rationale,
        CancellationToken cancellationToken)
    {
        var snapshot = await PlanningLineage.TryLoadSnapshotAsync(dbContext, runId, cancellationToken);
        if (snapshot is not null
            && Decide(snapshot, runId, workspaceId, checkpoint, escalationMessageId, rationale) is { Existing: { } competing, Error: null })
        {
            return await SuccessForExistingAsync(runId, competing.Grant, cancellationToken);
        }

        return Failure(Error.Failure("planning_authorizations.persistence_failed", "The authorization could not be durably recorded."));
    }

    private async Task<Result<AuthorizePlanningImplementationCommandResult>> SuccessForExistingAsync(
        Guid runId, PlanningImplementationAuthorization existing, CancellationToken cancellationToken)
    {
        var sequence = await dbContext.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId
                && candidate.EventType == RunEventType.CollaborationMessageRecorded
                && candidate.PayloadJson.Contains(existing.HumanInstructionMessageId.ToString()))
            .Select(candidate => (long?)candidate.Sequence)
            .SingleOrDefaultAsync(cancellationToken);
        if (sequence is not { } latest)
        {
            return Failure(PlanningImplementationAuthorizationErrors.EventMissing());
        }

        if (eventNotifier is not null)
        {
            await eventNotifier.NotifyRunAdvancedAsync(runId, latest, cancellationToken);
        }

        return Result<AuthorizePlanningImplementationCommandResult>.Success(
            new(existing.Id, existing.EscalationMessageId, existing.FinalProposalMessageId, existing.HumanInstructionMessageId,
                "Authorized", latest));
    }

    private static async Task RollbackBestEffortAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or ObjectDisposedException)
        {
            // A disposed, uncommitted transaction rolls back on its own.
        }
    }

    private static Result<AuthorizePlanningImplementationCommandResult> Failure(Error error) =>
        Result<AuthorizePlanningImplementationCommandResult>.Failure(error);
}
