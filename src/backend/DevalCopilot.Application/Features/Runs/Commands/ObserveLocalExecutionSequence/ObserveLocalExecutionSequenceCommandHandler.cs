using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ObserveLocalExecutionSequence;

/// <summary>
/// The coordinator's one decision step (ADR-0060). The transaction's first statement writes the run row, which takes the database write
/// lock, so a Stop, a claim, a classification or a restart that committed first is read here and one that arrives later contends on the
/// revisions this save advances. Every decision is a function of locked durable state and the clock: nothing is inferred from an earlier
/// tick, the loaded UI, a latest message or process knowledge. A deadline never proves process death and never cancels or releases an
/// operation; it only closes successor authority.
/// </summary>
public sealed class ObserveLocalExecutionSequenceCommandHandler(
    IDevalCopilotDbContext dbContext, TimeProvider timeProvider, IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<ObserveLocalExecutionSequenceCommand, Result<ObserveLocalExecutionSequenceCommandResult>>
{
    public async Task<Result<ObserveLocalExecutionSequenceCommandResult>> HandleAsync(
        ObserveLocalExecutionSequenceCommand command, CancellationToken cancellationToken)
    {
        var known = await dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(candidate => candidate.Id == command.SequenceId)
            .Select(candidate => new { candidate.State, candidate.RunId })
            .SingleOrDefaultAsync(cancellationToken);
        if (known is null)
        {
            return Failure(LocalExecutionSequenceErrors.NotFound());
        }

        if (!LocalExecutionSequencePolicy.IsAuthorityLive(known.State))
        {
            return Success(known.RunId, known.State, changed: false, ticket: null);
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        // The run row, never the sequence row: an ended sequence is immutable by database guard, and even a no-op write to it is refused.
        var locked = await dbContext.Runs
            .Where(candidate => candidate.Id == known.RunId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Lifecycle, candidate => candidate.Lifecycle), cancellationToken);
        if (locked != 1)
        {
            return Failure(LocalExecutionSequenceErrors.NotFound());
        }

        var sequence = await dbContext.LocalExecutionSequences.SingleAsync(candidate => candidate.Id == command.SequenceId, cancellationToken);
        await dbContext.Entry(sequence).ReloadAsync(cancellationToken);
        if (!sequence.IsAuthorityLive)
        {
            return Success(sequence.RunId, sequence.State, changed: false, ticket: null);
        }

        var steps = await dbContext.LocalExecutionSequenceSteps
            .Where(candidate => candidate.SequenceId == sequence.Id)
            .OrderBy(candidate => candidate.Ordinal)
            .ToListAsync(cancellationToken);
        foreach (var step in steps)
        {
            await dbContext.Entry(step).ReloadAsync(cancellationToken);
        }

        var run = await dbContext.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == sequence.RunId, cancellationToken);
        var nowUtc = timeProvider.GetUtcNow();
        var events = new List<RunEvent>();
        var added = new List<object>();
        LocalExecutionStepTicket? ticket = null;

        switch (sequence.State)
        {
            case LocalExecutionSequenceState.Armed:
                if (steps.Count != 0 || nowUtc >= sequence.AdvancementDeadlineUtc
                    || !await SourceHoldsAsync(sequence, run, sequence.CheckpointId, sequence.FingerprintSha256, cancellationToken))
                {
                    End(sequence, events, LocalExecutionSequenceState.NeedsAttention,
                        nowUtc >= sequence.AdvancementDeadlineUtc && steps.Count == 0
                            ? LocalExecutionSequenceReason.AdvancementDeadlineElapsed
                            : LocalExecutionSequenceReason.AuthorityChanged,
                        null, nowUtc);
                    break;
                }

                ticket = Reserve(
                    sequence, steps, added, events, LocalExecutionStepKind.Implementation, 0, null, null,
                    sequence.CheckpointId, sequence.FingerprintSha256, null, null, nowUtc);
                break;

            case LocalExecutionSequenceState.PreparingStep:
                ObservePreparation(sequence, steps, events, nowUtc);
                break;

            default:
                ticket = await ObserveClaimedStepAsync(sequence, steps, run, events, added, nowUtc, cancellationToken);
                break;
        }

        if (events.Count == 0)
        {
            return Success(sequence.RunId, sequence.State, changed: false, ticket: null);
        }

        dbContext.Events.AddRange(events);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The revisions are the compare-and-set: a competing writer that committed after this decision read is never overwritten,
            // and no ticket is handed out for a reservation that did not commit.
            return Failure(LocalExecutionSequenceErrors.PersistenceFailed());
        }

        await NotifyAsync(sequence.RunId, events[^1].Sequence, cancellationToken);
        return Success(sequence.RunId, sequence.State, changed: true, ticket);
    }

    /// <summary>A reserved step whose two-minute preparation deadline (or the advancement deadline) elapsed: its authority is revoked.
    /// The claim commits only with the step still Reserved, so nothing was claimed or consumed.</summary>
    private static void ObservePreparation(
        LocalExecutionSequence sequence, IReadOnlyList<LocalExecutionSequenceStep> steps, List<RunEvent> events, DateTimeOffset nowUtc)
    {
        var reserved = steps.SingleOrDefault(candidate => candidate.State == LocalExecutionSequenceStepState.Reserved);
        if (reserved is null)
        {
            End(sequence, events, LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.OperationOutcomeInvalid, null, nowUtc);
            return;
        }

        var advancementElapsed = nowUtc >= sequence.AdvancementDeadlineUtc;
        if (!advancementElapsed && nowUtc < reserved.PreparationDeadlineUtc)
        {
            return;
        }

        var reason = advancementElapsed
            ? LocalExecutionSequenceReason.AdvancementDeadlineElapsed
            : LocalExecutionSequenceReason.PreparationExpired;
        reserved.Revoke(reason, nowUtc);
        End(sequence, events, LocalExecutionSequenceState.NeedsAttention, reason, null, nowUtc);
    }

    /// <summary>AwaitingStepResult or Stopping: the claimed step's operation is observed, never fabricated, terminated or released.</summary>
    private async Task<LocalExecutionStepTicket?> ObserveClaimedStepAsync(
        LocalExecutionSequence sequence,
        IReadOnlyList<LocalExecutionSequenceStep> steps,
        Run run,
        List<RunEvent> events,
        List<object> added,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var claimed = steps.SingleOrDefault(candidate => candidate.State == LocalExecutionSequenceStepState.Claimed);
        if (claimed is null || claimed.OperationId is not { } operationId || claimed.ResultDeadlineUtc is not { } deadline)
        {
            End(sequence, events, LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.OperationOutcomeInvalid, null, nowUtc);
            return null;
        }

        var operation = await ReadOperationAsync(sequence, claimed, operationId, cancellationToken);
        if (operation is null)
        {
            End(sequence, events, LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.OperationOutcomeInvalid, null, nowUtc);
            return null;
        }

        if (!operation.IsTerminal)
        {
            // The result deadline is a coordination observation, not measured invocation time or proof of process death. Expiry closes
            // advancement; it never marks the operation terminal, cancels its process or releases its ownership.
            if (nowUtc >= deadline)
            {
                End(sequence, events, LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.ResultDeadlineElapsed, null, nowUtc);
                return null;
            }

            // A claim that no ordinary workspace or global capacity gate currently lets dispatch ends advancement with attention (or the
            // requested stop) instead of waiting in a queue: the ended sequence then refuses its dispatch and the dedicated refusal is recorded.
            if (!operation.IsDispatched && await DispatchIsRefusedByFencesAsync(sequence, claimed, operationId, cancellationToken))
            {
                End(
                    sequence,
                    events,
                    sequence.State == LocalExecutionSequenceState.Stopping ? LocalExecutionSequenceState.Stopped : LocalExecutionSequenceState.NeedsAttention,
                    sequence.State == LocalExecutionSequenceState.Stopping
                        ? LocalExecutionSequenceReason.StoppedAfterOperation
                        : LocalExecutionSequenceReason.CapacityRefused,
                    null,
                    nowUtc);
            }

            return null;
        }

        // The operation is terminal. A late result can finish its own row but never revives or advances a closed sequence, and a Stop that
        // won before classification keeps Stopped even if the provider produced a valid result.
        claimed.Conclude(nowUtc);
        events.Add(LocalExecutionSequenceEvents.StepConcluded(sequence, claimed, operation.Status, operation.Outcome, nowUtc));

        if (sequence.State == LocalExecutionSequenceState.Stopping)
        {
            End(sequence, events, LocalExecutionSequenceState.Stopped, LocalExecutionSequenceReason.StoppedAfterOperation, null, nowUtc);
            return null;
        }

        if (operation.CompletedAtUtc is not { } completedAtUtc || completedAtUtc >= deadline || nowUtc >= sequence.AdvancementDeadlineUtc)
        {
            End(sequence, events, LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.ResultDeadlineElapsed, null, nowUtc);
            return null;
        }

        var verdict = await LocalExecutionOutcomeClassifier.ClassifyAsync(dbContext, sequence, steps, claimed, run, cancellationToken);
        switch (verdict.Kind)
        {
            case LocalExecutionVerdictKind.Approved:
                End(
                    sequence, events, LocalExecutionSequenceState.ReadyForHumanApproval, LocalExecutionSequenceReason.ReviewApproved,
                    null, nowUtc, verdict.ReportMessageId, verdict.ReviewAttemptId);
                return null;

            case LocalExecutionVerdictKind.ProviderEscalation:
                End(
                    sequence, events, LocalExecutionSequenceState.HumanEscalation, LocalExecutionSequenceReason.DiagnosisEscalated,
                    null, nowUtc, verdict.ReportMessageId, null, verdict.EscalationMessageId);
                return null;

            case LocalExecutionVerdictKind.AllowanceReached:
                {
                    var message = HostExhaustionEscalation(sequence.RunId, verdict.ReportMessageId!.Value, nowUtc);
                    dbContext.CollaborationMessages.Add(message);
                    events.Add(RunEvent.Record(
                        Guid.NewGuid(), sequence.RunId, null, RunEventType.CollaborationMessageRecorded, message.Actor,
                        "{\"messageId\":\"" + message.Id + "\",\"type\":\"Escalation\",\"provenance\":\"HostConstructed\"}", nowUtc));
                    End(
                        sequence, events, LocalExecutionSequenceState.HumanEscalation, LocalExecutionSequenceReason.AllowanceReached,
                        null, nowUtc, verdict.ReportMessageId, null, message.Id);
                    return null;
                }

            case LocalExecutionVerdictKind.Reserve:
                if (!await SourceHoldsAsync(sequence, run, verdict.SourceCheckpointId!.Value, verdict.SourceFingerprintSha256!, cancellationToken))
                {
                    End(sequence, events, LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.AuthorityChanged, null, nowUtc);
                    return null;
                }

                return Reserve(
                    sequence, steps, added, events, verdict.NextKind, verdict.PassNumber, verdict.RecipeOrdinal, claimed.Id,
                    verdict.SourceCheckpointId.Value, verdict.SourceFingerprintSha256!, verdict.ReportMessageId, verdict.SubjectAttemptId, nowUtc);

            default:
                End(sequence, events, LocalExecutionSequenceState.NeedsAttention, verdict.Reason!, verdict.Detail, nowUtc);
                return null;
        }
    }

    private LocalExecutionStepTicket Reserve(
        LocalExecutionSequence sequence,
        IReadOnlyList<LocalExecutionSequenceStep> steps,
        List<object> added,
        List<RunEvent> events,
        LocalExecutionStepKind kind,
        int pass,
        int? recipeOrdinal,
        Guid? predecessorStepId,
        Guid sourceCheckpointId,
        string sourceFingerprintSha256,
        Guid? reportMessageId,
        Guid? subjectAttemptId,
        DateTimeOffset nowUtc)
    {
        var ordinal = steps.Count == 0 ? 0 : steps.Max(step => step.Ordinal) + 1;
        var step = LocalExecutionSequenceStep.Reserve(
            Guid.NewGuid(),
            sequence.Id,
            ordinal,
            kind,
            pass,
            recipeOrdinal,
            predecessorStepId,
            sourceCheckpointId,
            sourceFingerprintSha256,
            reportMessageId,
            subjectAttemptId,
            reservedOperationId: Guid.NewGuid(),
            preparationOwnerToken: Guid.NewGuid(),
            nowUtc);
        dbContext.LocalExecutionSequenceSteps.Add(step);
        added.Add(step);
        sequence.BeginPreparing(nowUtc);
        events.Add(LocalExecutionSequenceEvents.StepReserved(sequence, step, nowUtc));
        return new LocalExecutionStepTicket(
            sequence.Id,
            step.Id,
            ordinal,
            kind,
            step.ReservedOperationId,
            step.PreparationOwnerToken,
            sourceCheckpointId,
            reportMessageId,
            subjectAttemptId,
            null);
    }

    /// <summary>Whether the exact source a step would run against still holds: the run is still a running manual Agent run, the project's
    /// latest workspace is still the pinned, Ready, owned one with its active lease and its latest checkpoint is exactly the expected
    /// result, and the pinned recipes and requested settings are unchanged. A newer workspace or an unrelated checkpoint is never followed.</summary>
    private async Task<bool> SourceHoldsAsync(
        LocalExecutionSequence sequence, Run run, Guid expectedCheckpointId, string expectedFingerprint, CancellationToken cancellationToken)
    {
        if (run.Id != sequence.RunId
            || run.ProjectId != sequence.ProjectId
            || run.Lifecycle != RunLifecycle.Running
            || await CurrentRunExecutionMode.ReadAsync(dbContext, run.Id, cancellationToken) != RunExecutionMode.ManualAgent
            || !string.Equals(LocalExecutionSettingsSnapshot.Of(run), sequence.SettingsSnapshot, StringComparison.Ordinal))
        {
            return false;
        }

        var workspace = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Id != sequence.WorkspaceId || workspace.Status != WorkspaceStatus.Ready)
        {
            return false;
        }

        if (!await dbContext.RepositoryMutationLeases.AsNoTracking().AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return false;
        }

        var checkpoint = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        return checkpoint is not null
            && checkpoint.Id == expectedCheckpointId
            && string.Equals(checkpoint.FingerprintSha256, expectedFingerprint, StringComparison.Ordinal)
            && await LocalExecutionStepAuthority.RecipesHoldAsync(dbContext, sequence, cancellationToken);
    }

    private async Task<OperationReading?> ReadOperationAsync(
        LocalExecutionSequence sequence, LocalExecutionSequenceStep step, Guid operationId, CancellationToken cancellationToken)
    {
        if (step.IsAgent)
        {
            var attempt = await dbContext.Attempts.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == operationId, cancellationToken);
            return attempt is null || attempt.RunId != sequence.RunId
                ? null
                : new OperationReading(
                    attempt.Status != AttemptStatus.Running,
                    attempt.AgentDispatchedAtUtc is not null,
                    attempt.CompletedAtUtc,
                    attempt.Status.ToString(),
                    attempt.AgentOutcome?.ToString());
        }

        var execution = await dbContext.VerificationExecutions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == operationId, cancellationToken);
        return execution is null || execution.ProjectId != sequence.ProjectId
            ? null
            : new OperationReading(
                execution.Status != VerificationExecutionStatus.Running,
                execution.DispatchedAtUtc is not null,
                execution.CompletedAtUtc,
                execution.Status.ToString(),
                execution.Outcome?.ToString());
    }

    private async Task<bool> DispatchIsRefusedByFencesAsync(
        LocalExecutionSequence sequence, LocalExecutionSequenceStep step, Guid operationId, CancellationToken cancellationToken)
    {
        if (step.IsAgent)
        {
            var attempt = await dbContext.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == operationId, cancellationToken);
            return await WorkspaceExecutionFence.CheckAgentDispatchAsync(dbContext, sequence.ProjectId, sequence.WorkspaceId, operationId, cancellationToken) is not null
                || await ExecutionCapacityFence.CheckAgentDispatchAsync(dbContext, attempt, cancellationToken) is not null;
        }

        return await WorkspaceExecutionFence.CheckVerificationDispatchAsync(dbContext, sequence.ProjectId, sequence.WorkspaceId, operationId, cancellationToken) is not null
            || await ExecutionCapacityFence.CheckDispatchAsync(
                dbContext, new ExecutionOwner(ExecutionOwnerKind.Verification, operationId), cancellationToken) is not null;
    }

    private static CollaborationMessage HostExhaustionEscalation(Guid runId, Guid reportMessageId, DateTimeOffset nowUtc) =>
        CollaborationMessage.Record(
            Guid.NewGuid(),
            runId,
            null,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForOrchestrator(),
            ParticipantIdentity.ForHuman(),
            CollaborationMessageType.Escalation,
            reportMessageId,
            "Local execution cycle requires a human decision.",
            JsonSerializer.Serialize(new
            {
                unresolvedDecision = "The authorized local execution cycle has unresolved findings.",
                options = "Inspect the recorded evidence and decide on an explicit manual next action. This cycle cannot continue.",
                consequences = "No further operation is authorized by this sequence; the run remains awaiting human action.",
                evidence = "The authorized automatic correction allowance was reached.",
                recommendedChoice = "Inspect the evidence before requesting any manual continuation.",
            }),
            CollaborationMessageProvenance.HostConstructed,
            nowUtc);

    private static void End(
        LocalExecutionSequence sequence,
        List<RunEvent> events,
        LocalExecutionSequenceState endState,
        string reasonCode,
        string? detailCode,
        DateTimeOffset nowUtc,
        Guid? finalReportMessageId = null,
        Guid? finalReviewAttemptId = null,
        Guid? finalEscalationMessageId = null)
    {
        sequence.End(endState, reasonCode, detailCode, nowUtc, finalReportMessageId, finalReviewAttemptId, finalEscalationMessageId);
        events.Add(LocalExecutionSequenceEvents.Ended(sequence, nowUtc));
    }

    private static Result<ObserveLocalExecutionSequenceCommandResult> Success(
        Guid runId, LocalExecutionSequenceState state, bool changed, LocalExecutionStepTicket? ticket) =>
        Result<ObserveLocalExecutionSequenceCommandResult>.Success(new ObserveLocalExecutionSequenceCommandResult(runId, state, changed, ticket));

    private static Result<ObserveLocalExecutionSequenceCommandResult> Failure(Error error) =>
        Result<ObserveLocalExecutionSequenceCommandResult>.Failure(error);

    /// <summary>Run-advance notifications are only hints (ADR-0003): a notifier failure after the commit never turns the recorded
    /// transition into a reported failure. Cancellation still propagates.</summary>
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

    private sealed record OperationReading(bool IsTerminal, bool IsDispatched, DateTimeOffset? CompletedAtUtc, string Status, string? Outcome);
}
