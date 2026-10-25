using System.Linq.Expressions;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// ADR-0065: the one database-side relational proof that a terminal linked local execution operation's retained quarantine was released
/// by the Human. It is the ownership-side twin of the SQL fence predicate of migration <c>AddLocalExecutionOwnershipRelease</c>, and every
/// shared consumer (workspace ownership, global capacity, manual-control fences, abandonment and the final admission and dispatch gates)
/// reaches it only through <see cref="LocalExecutionOwnership"/>.
///
/// <para>
/// A release subtracts an owner only when ONE chain is complete and coherent: the release names a witness, its one observation, the one
/// step of the operation and the one Human event header; the witness equals the step's full tuple, its sequence, run, project and
/// workspace, the operation row (run, workspace, source, contract, dispatch time) and the queued admission (exactly Consumed) or its
/// absence (an immediate cycle); the observation equals the operation's stored terminal process tuple; the operation is in a recognized
/// terminal status of a sequence recorded Stopped, NeedsAttention or Interrupted. A receipt identifier, an observation alone, a Human
/// event alone or a client flag never subtracts anything, and a malformed, foreign or duplicate chain proves nothing, so the operation
/// keeps owning and <see cref="HasMalformedReleaseAsync"/> makes the shared readings Unavailable instead of a convenient zero.
/// </para>
///
/// <para>
/// The chain is the complete historical proof of the release reading (correction LQR-R1/LQR-R2): the run is a ManualAgent run of the
/// sequence's own project, the workspace and the source checkpoint (workspace and fingerprint) are the step's own, the sequence's recipe
/// and step counts are structurally sound, an Immediate cycle has no admission for the step and neither family's identity collides with
/// the other, the observation never outlasts its process and is recorded no earlier than the dispatch, the receipt is recorded no earlier
/// than the observation and expires after it, and the terminal result is one the existing Domain result policies could have recorded for
/// a timed-out or cancelled process (<see cref="LocalExecutionReleaseTerminal"/>). Times are compared as their stored UTC text because the
/// SQLite provider cannot translate a DateTimeOffset comparison; that ordering equals the chronological one for the normalized UTC text the
/// host writes. The two limits the database cannot express are kept elsewhere on purpose: the two-minute receipt lifetime is a table CHECK
/// of the migration, and the receipt's SHA-256 confirmation (SQLite cannot compute one) is verified by the release reading, the replay and
/// the locked command, which bind it to exactly these proved facts.
/// </para>
/// </summary>
internal static class LocalExecutionReleaseChain
{
    /// <summary>The complete coherent release chain of the Agent attempt, evidenced by the given release. The two parameters are
    /// substituted at construction, so each consumer receives one inline translatable predicate.</summary>
    public static Expression<Func<Attempt, LocalExecutionOwnershipRelease, bool>> AgentChain(IDevalCopilotDbContext dbContext)
    {
        var releasableOutcomes = LocalExecutionReleaseTerminal.AgentOutcomes.ToArray();
        return (attempt, release) => dbContext.LocalExecutionDispatchWitnesses.Any(witness => witness.Id == release.WitnessId
            && witness.OperationFamily == LocalExecutionOperationFamily.Agent
            && witness.AgentAttemptId == attempt.Id
            && witness.VerificationExecutionId == null
            && witness.AgentResponseContract == attempt.AgentResponseContract
            && witness.DispatchedAtUtc == attempt.AgentDispatchedAtUtc
            && attempt.Status == AttemptStatus.Failed
            && attempt.AgentOutcome != null
            && releasableOutcomes.Contains(attempt.AgentOutcome.Value)
            && (attempt.AgentProcessOutcome == ProcessOutcome.TimedOut || attempt.AgentProcessOutcome == ProcessOutcome.Cancelled)
            && attempt.AgentProcessExitCode == null
            && attempt.AgentProcessDuration >= TimeSpan.Zero
            && attempt.RunId == witness.RunId
            && attempt.AgentGitWorkspaceId == witness.WorkspaceId
            && attempt.AgentGitCheckpointId == witness.SourceCheckpointId
            && attempt.AgentCheckpointFingerprintSha256 == witness.SourceFingerprintSha256
            && !dbContext.VerificationExecutions.Any(collision => collision.Id == attempt.Id)
            && release.IntentVersion == LocalExecutionOwnershipRelease.CurrentIntentVersion
            && release.IntentConfirmationVersion == release.ConfirmationVersion
            && release.IntentConfirmationExpiresAtUtc == release.ConfirmationExpiresAtUtc
            && release.IntentFamily == LocalExecutionOperationFamily.Agent
            && release.IntentOperationId == attempt.Id
            && release.IntentOperationStatus == nameof(AttemptStatus.Failed)
            && release.IntentAgentOutcome == attempt.AgentOutcome
            && release.IntentAgentResponseContract == attempt.AgentResponseContract
            && release.IntentCompletionFingerprintSha256 == null
            && release.IntentRecipeCommandId == null
            && release.IntentRunId == attempt.RunId
            && release.IntentWorkspaceId == attempt.AgentGitWorkspaceId
            && release.IntentSourceCheckpointId == attempt.AgentGitCheckpointId
            && release.IntentSourceFingerprintSha256 == attempt.AgentCheckpointFingerprintSha256
            && release.IntentDispatchedAtUtc == attempt.AgentDispatchedAtUtc
            && release.IntentAdmissionId == witness.AdmissionId
            && release.IntentProcessOutcome == attempt.AgentProcessOutcome
            && release.IntentSemanticExitCode == attempt.AgentProcessExitCode
            && release.IntentProcessDuration == attempt.AgentProcessDuration
            && dbContext.LocalExecutionTerminationObservations.Any(observation => observation.Id == release.ObservationId
                && observation.WitnessId == witness.Id
                && attempt.AgentProcessOutcome == observation.ProcessOutcome
                && attempt.AgentProcessExitCode == observation.SemanticExitCode
                && attempt.AgentProcessDuration == observation.ProcessDuration
                && observation.ObservedElapsed <= observation.ProcessDuration
                && string.Compare((string)(object)observation.RecordedAtUtc, (string)(object)witness.DispatchedAtUtc) >= 0
                && release.ConfirmationVersion == LocalExecutionOwnershipReleaseConfirmation.CurrentVersion
                && string.Compare((string)(object)release.ReleasedAtUtc, (string)(object)observation.RecordedAtUtc) >= 0
                && string.Compare((string)(object)release.ConfirmationExpiresAtUtc, (string)(object)release.ReleasedAtUtc) > 0
                && release.IntentProcessOutcome == observation.ProcessOutcome
                && release.IntentSemanticExitCode == observation.SemanticExitCode
                && release.IntentProcessDuration == observation.ProcessDuration
                && release.IntentPhysicalExitCode == observation.PhysicalExitCode
                && release.IntentObservedElapsed == observation.ObservedElapsed
                && release.IntentObservationRecordedAtUtc == observation.RecordedAtUtc)
            && dbContext.LocalExecutionSequenceSteps.Any(step => step.Id == witness.StepId
                && step.Id == release.StepId
                && step.OperationId == attempt.Id
                && (step.State == LocalExecutionSequenceStepState.Claimed || step.State == LocalExecutionSequenceStepState.Concluded)
                && step.Kind == witness.Kind
                && step.Ordinal == witness.Ordinal
                && step.PassNumber == witness.PassNumber
                && step.RecipeOrdinal == witness.RecipeOrdinal
                && step.PredecessorStepId == witness.PredecessorStepId
                && step.SourceCheckpointId == witness.SourceCheckpointId
                && step.SourceFingerprintSha256 == witness.SourceFingerprintSha256
                && step.ReportMessageId == witness.ReportMessageId
                && step.SubjectAttemptId == witness.SubjectAttemptId
                && release.IntentKind == step.Kind
                && release.IntentOrdinal == step.Ordinal
                && release.IntentPassNumber == step.PassNumber
                && release.IntentRecipeOrdinal == step.RecipeOrdinal
                && release.IntentPredecessorStepId == step.PredecessorStepId
                && release.IntentReportMessageId == step.ReportMessageId
                && release.IntentSubjectAttemptId == step.SubjectAttemptId
                && release.IntentSourceCheckpointId == step.SourceCheckpointId
                && release.IntentSourceFingerprintSha256 == step.SourceFingerprintSha256
                && ((step.Kind == LocalExecutionStepKind.Implementation && attempt.AgentResponseContract == AgentResponseContract.ImplementationReport)
                    || (step.Kind == LocalExecutionStepKind.CodeReview && attempt.AgentResponseContract == AgentResponseContract.ImplementationReview)
                    || (step.Kind == LocalExecutionStepKind.VerificationDiagnosis
                        && attempt.AgentResponseContract == AgentResponseContract.VerificationDiagnosis)
                    || ((step.Kind == LocalExecutionStepKind.ReviewCorrection || step.Kind == LocalExecutionStepKind.DiagnosisCorrection)
                        && attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection))
                && dbContext.LocalExecutionSequences.Any(sequence => sequence.Id == step.SequenceId
                    && witness.SequenceId == sequence.Id
                    && witness.RunId == sequence.RunId
                    && witness.ProjectId == sequence.ProjectId
                    && witness.WorkspaceId == sequence.WorkspaceId
                    && (sequence.State == LocalExecutionSequenceState.Stopped
                        || sequence.State == LocalExecutionSequenceState.NeedsAttention
                        || sequence.State == LocalExecutionSequenceState.Interrupted)
                    && release.IntentSequenceId == sequence.Id
                    && release.IntentRunId == sequence.RunId
                    && release.IntentProjectId == sequence.ProjectId
                    && release.IntentWorkspaceId == sequence.WorkspaceId
                    && release.IntentSequenceState == sequence.State
                    && release.IntentAdmissionMode == sequence.AdmissionMode
                    && dbContext.Runs.Any(run => run.Id == sequence.RunId
                        && run.ProjectId == sequence.ProjectId
                        && EF.Property<string>(run, Run.ExecutionModeStorageProperty) == RunExecutionModeStorage.ManualAgentText)
                    && dbContext.GitWorkspaces.Any(workspace => workspace.Id == sequence.WorkspaceId && workspace.ProjectId == sequence.ProjectId)
                    && dbContext.GitCheckpoints.Any(source => source.Id == step.SourceCheckpointId
                        && source.WorkspaceId == sequence.WorkspaceId
                        && source.FingerprintSha256 == step.SourceFingerprintSha256)
                    && dbContext.LocalExecutionSequenceRecipes.Count(counted => counted.SequenceId == sequence.Id) == sequence.RecipeCount
                    && dbContext.LocalExecutionSequenceSteps.Count(counted => counted.SequenceId == sequence.Id)
                        <= 3 * sequence.RecipeCount + 6
                    && ((sequence.AdmissionMode == LocalExecutionAdmissionMode.Immediate
                            && witness.AdmissionId == null
                            && !dbContext.LocalExecutionAdmissions.Any(unexpected => unexpected.StepId == step.Id))
                        || (sequence.AdmissionMode == LocalExecutionAdmissionMode.WaitForAdmission
                            && dbContext.LocalExecutionAdmissions.Any(admission => admission.Id == witness.AdmissionId
                                && admission.StepId == step.Id
                                && admission.SequenceId == sequence.Id
                                && admission.State == LocalExecutionAdmissionState.Consumed
                                && admission.Ordinal == step.Ordinal
                                && admission.Kind == step.Kind
                                && admission.PassNumber == step.PassNumber
                                && admission.RecipeOrdinal == step.RecipeOrdinal
                                && admission.SourceCheckpointId == step.SourceCheckpointId)))))
            && dbContext.Events.Any(runEvent => runEvent.Id == release.EventId
                && runEvent.RunId == witness.RunId
                && runEvent.EventType == RunEventType.LocalExecutionOwnershipReleased
                && runEvent.ActorKind == ParticipantKind.Human));
    }

    /// <summary>The complete coherent release chain of the verification execution, evidenced by the given release.</summary>
    public static Expression<Func<VerificationExecution, LocalExecutionOwnershipRelease, bool>> ExecutionChain(IDevalCopilotDbContext dbContext) =>
        (execution, release) => dbContext.LocalExecutionDispatchWitnesses.Any(witness => witness.Id == release.WitnessId
            && witness.OperationFamily == LocalExecutionOperationFamily.Verification
            && witness.VerificationExecutionId == execution.Id
            && witness.AgentAttemptId == null
            && witness.RecipeCommandId == execution.VerificationCommandId
            && witness.DispatchedAtUtc == execution.DispatchedAtUtc
            && (execution.Outcome == VerificationExecutionOutcome.TimedOut || execution.Outcome == VerificationExecutionOutcome.Cancelled)
            && execution.ExitCode == null
            && execution.CompletionFingerprintSha256 != null
            && ((execution.Status == VerificationExecutionStatus.TimedOut
                    && execution.Outcome == VerificationExecutionOutcome.TimedOut
                    && execution.CompletionFingerprintSha256 == execution.CheckpointFingerprintSha256)
                || (execution.Status == VerificationExecutionStatus.Cancelled
                    && execution.Outcome == VerificationExecutionOutcome.Cancelled
                    && execution.CompletionFingerprintSha256 == execution.CheckpointFingerprintSha256)
                || (execution.Status == VerificationExecutionStatus.SourceChanged
                    && execution.CompletionFingerprintSha256 != execution.CheckpointFingerprintSha256))
            && !dbContext.Attempts.Any(collision => collision.Id == execution.Id)
            && release.IntentVersion == LocalExecutionOwnershipRelease.CurrentIntentVersion
            && release.IntentConfirmationVersion == release.ConfirmationVersion
            && release.IntentConfirmationExpiresAtUtc == release.ConfirmationExpiresAtUtc
            && release.IntentFamily == LocalExecutionOperationFamily.Verification
            && release.IntentOperationId == execution.Id
            && ((execution.Status == VerificationExecutionStatus.TimedOut && release.IntentOperationStatus == nameof(VerificationExecutionStatus.TimedOut))
                || (execution.Status == VerificationExecutionStatus.Cancelled && release.IntentOperationStatus == nameof(VerificationExecutionStatus.Cancelled))
                || (execution.Status == VerificationExecutionStatus.SourceChanged
                    && release.IntentOperationStatus == nameof(VerificationExecutionStatus.SourceChanged)))
            && release.IntentAgentOutcome == null
            && release.IntentAgentResponseContract == null
            && release.IntentCompletionFingerprintSha256 == execution.CompletionFingerprintSha256
            && release.IntentRecipeCommandId == execution.VerificationCommandId
            && release.IntentProjectId == execution.ProjectId
            && release.IntentWorkspaceId == execution.GitWorkspaceId
            && release.IntentSourceCheckpointId == execution.GitCheckpointId
            && release.IntentSourceFingerprintSha256 == execution.CheckpointFingerprintSha256
            && release.IntentDispatchedAtUtc == execution.DispatchedAtUtc
            && release.IntentAdmissionId == witness.AdmissionId
            && release.IntentSemanticExitCode == execution.ExitCode
            && ((execution.Outcome == VerificationExecutionOutcome.TimedOut && release.IntentProcessOutcome == ProcessOutcome.TimedOut)
                || (execution.Outcome == VerificationExecutionOutcome.Cancelled && release.IntentProcessOutcome == ProcessOutcome.Cancelled))
            && execution.ProjectId == witness.ProjectId
            && execution.GitWorkspaceId == witness.WorkspaceId
            && execution.GitCheckpointId == witness.SourceCheckpointId
            && execution.CheckpointFingerprintSha256 == witness.SourceFingerprintSha256
            && dbContext.LocalExecutionTerminationObservations.Any(observation => observation.Id == release.ObservationId
                && observation.WitnessId == witness.Id
                && execution.ExitCode == observation.SemanticExitCode
                && ((execution.Outcome == VerificationExecutionOutcome.TimedOut && observation.ProcessOutcome == ProcessOutcome.TimedOut)
                    || (execution.Outcome == VerificationExecutionOutcome.Cancelled && observation.ProcessOutcome == ProcessOutcome.Cancelled))
                && observation.ObservedElapsed <= observation.ProcessDuration
                && string.Compare((string)(object)observation.RecordedAtUtc, (string)(object)witness.DispatchedAtUtc) >= 0
                && release.ConfirmationVersion == LocalExecutionOwnershipReleaseConfirmation.CurrentVersion
                && string.Compare((string)(object)release.ReleasedAtUtc, (string)(object)observation.RecordedAtUtc) >= 0
                && string.Compare((string)(object)release.ConfirmationExpiresAtUtc, (string)(object)release.ReleasedAtUtc) > 0
                && release.IntentProcessOutcome == observation.ProcessOutcome
                && release.IntentSemanticExitCode == observation.SemanticExitCode
                && release.IntentProcessDuration == observation.ProcessDuration
                && release.IntentPhysicalExitCode == observation.PhysicalExitCode
                && release.IntentObservedElapsed == observation.ObservedElapsed
                && release.IntentObservationRecordedAtUtc == observation.RecordedAtUtc)
            && dbContext.LocalExecutionSequenceSteps.Any(step => step.Id == witness.StepId
                && step.Id == release.StepId
                && step.OperationId == execution.Id
                && step.Kind == LocalExecutionStepKind.Verification
                && (step.State == LocalExecutionSequenceStepState.Claimed || step.State == LocalExecutionSequenceStepState.Concluded)
                && step.Kind == witness.Kind
                && step.Ordinal == witness.Ordinal
                && step.PassNumber == witness.PassNumber
                && step.RecipeOrdinal == witness.RecipeOrdinal
                && step.PredecessorStepId == witness.PredecessorStepId
                && step.SourceCheckpointId == witness.SourceCheckpointId
                && step.SourceFingerprintSha256 == witness.SourceFingerprintSha256
                && step.ReportMessageId == witness.ReportMessageId
                && step.SubjectAttemptId == witness.SubjectAttemptId
                && release.IntentKind == step.Kind
                && release.IntentOrdinal == step.Ordinal
                && release.IntentPassNumber == step.PassNumber
                && release.IntentRecipeOrdinal == step.RecipeOrdinal
                && release.IntentPredecessorStepId == step.PredecessorStepId
                && release.IntentReportMessageId == step.ReportMessageId
                && release.IntentSubjectAttemptId == step.SubjectAttemptId
                && release.IntentSourceCheckpointId == step.SourceCheckpointId
                && release.IntentSourceFingerprintSha256 == step.SourceFingerprintSha256
                && dbContext.LocalExecutionSequences.Any(sequence => sequence.Id == step.SequenceId
                    && witness.SequenceId == sequence.Id
                    && witness.RunId == sequence.RunId
                    && witness.ProjectId == sequence.ProjectId
                    && witness.WorkspaceId == sequence.WorkspaceId
                    && (sequence.State == LocalExecutionSequenceState.Stopped
                        || sequence.State == LocalExecutionSequenceState.NeedsAttention
                        || sequence.State == LocalExecutionSequenceState.Interrupted)
                    && release.IntentSequenceId == sequence.Id
                    && release.IntentRunId == sequence.RunId
                    && release.IntentProjectId == sequence.ProjectId
                    && release.IntentWorkspaceId == sequence.WorkspaceId
                    && release.IntentSequenceState == sequence.State
                    && release.IntentAdmissionMode == sequence.AdmissionMode
                    && dbContext.Runs.Any(run => run.Id == sequence.RunId
                        && run.ProjectId == sequence.ProjectId
                        && EF.Property<string>(run, Run.ExecutionModeStorageProperty) == RunExecutionModeStorage.ManualAgentText)
                    && dbContext.GitWorkspaces.Any(workspace => workspace.Id == sequence.WorkspaceId && workspace.ProjectId == sequence.ProjectId)
                    && dbContext.GitCheckpoints.Any(source => source.Id == step.SourceCheckpointId
                        && source.WorkspaceId == sequence.WorkspaceId
                        && source.FingerprintSha256 == step.SourceFingerprintSha256)
                    && dbContext.LocalExecutionSequenceRecipes.Count(counted => counted.SequenceId == sequence.Id) == sequence.RecipeCount
                    && dbContext.LocalExecutionSequenceSteps.Count(counted => counted.SequenceId == sequence.Id)
                        <= 3 * sequence.RecipeCount + 6
                    && dbContext.LocalExecutionSequenceRecipes.Any(recipe => recipe.SequenceId == sequence.Id
                        && recipe.Ordinal == step.RecipeOrdinal
                        && recipe.VerificationCommandId == witness.RecipeCommandId)
                    && ((sequence.AdmissionMode == LocalExecutionAdmissionMode.Immediate
                            && witness.AdmissionId == null
                            && !dbContext.LocalExecutionAdmissions.Any(unexpected => unexpected.StepId == step.Id))
                        || (sequence.AdmissionMode == LocalExecutionAdmissionMode.WaitForAdmission
                            && dbContext.LocalExecutionAdmissions.Any(admission => admission.Id == witness.AdmissionId
                                && admission.StepId == step.Id
                                && admission.SequenceId == sequence.Id
                                && admission.State == LocalExecutionAdmissionState.Consumed
                                && admission.Ordinal == step.Ordinal
                                && admission.Kind == step.Kind
                                && admission.PassNumber == step.PassNumber
                                && admission.RecipeOrdinal == step.RecipeOrdinal
                                && admission.SourceCheckpointId == step.SourceCheckpointId)))))
            && dbContext.Events.Any(runEvent => runEvent.Id == release.EventId
                && runEvent.RunId == witness.RunId
                && runEvent.EventType == RunEventType.LocalExecutionOwnershipReleased
                && runEvent.ActorKind == ParticipantKind.Human));

    /// <summary>The Agent attempt has one coherent release chain.</summary>
    public static Expression<Func<Attempt, bool>> AgentReleased(IDevalCopilotDbContext dbContext)
    {
        var chain = AgentChain(dbContext);
        var attempt = Expression.Parameter(typeof(Attempt), "attempt");
        var release = Expression.Parameter(typeof(LocalExecutionOwnershipRelease), "release");
        var body = Replace(chain, attempt, release);
        return Expression.Lambda<Func<Attempt, bool>>(AnyRelease(dbContext, release, body), attempt);
    }

    /// <summary>The verification execution has one coherent release chain.</summary>
    public static Expression<Func<VerificationExecution, bool>> ExecutionReleased(IDevalCopilotDbContext dbContext)
    {
        var chain = ExecutionChain(dbContext);
        var execution = Expression.Parameter(typeof(VerificationExecution), "execution");
        var release = Expression.Parameter(typeof(LocalExecutionOwnershipRelease), "release");
        var body = Replace(chain, execution, release);
        return Expression.Lambda<Func<VerificationExecution, bool>>(AnyRelease(dbContext, release, body), execution);
    }

    /// <summary>
    /// Whether any release in scope is not a complete coherent chain of its own operation (a missing, foreign, duplicate or inconsistent
    /// witness, observation, step, event, admission or operation). Such evidence is never a zero count: the shared readings treat it as
    /// Unavailable while the operation keeps owning. <paramref name="projectId"/> scopes to one project, or null for every release.
    /// </summary>
    public static async Task<bool> HasMalformedReleaseAsync(
        IDevalCopilotDbContext dbContext, Guid? projectId, CancellationToken cancellationToken)
    {
        var releases = dbContext.LocalExecutionOwnershipReleases.AsNoTracking().AsQueryable();
        if (projectId is { } project)
        {
            releases = releases.Where(release => dbContext.LocalExecutionDispatchWitnesses.Any(
                witness => witness.Id == release.WitnessId && witness.ProjectId == project));
        }

        return await releases.CountAsync(cancellationToken) != await releases.CountAsync(Coherent(dbContext), cancellationToken);
    }

    /// <summary>The release is a complete coherent chain of some Agent attempt or of some verification execution.</summary>
    public static Expression<Func<LocalExecutionOwnershipRelease, bool>> Coherent(IDevalCopilotDbContext dbContext)
    {
        var release = Expression.Parameter(typeof(LocalExecutionOwnershipRelease), "release");
        var attempt = Expression.Parameter(typeof(Attempt), "attempt");
        var execution = Expression.Parameter(typeof(VerificationExecution), "execution");
        var anyAttempt = AnyOf(dbContext, nameof(IDevalCopilotDbContext.Attempts), attempt, Replace(AgentChain(dbContext), attempt, release));
        var anyExecution = AnyOf(
            dbContext, nameof(IDevalCopilotDbContext.VerificationExecutions), execution, Replace(ExecutionChain(dbContext), execution, release));
        return Expression.Lambda<Func<LocalExecutionOwnershipRelease, bool>>(Expression.OrElse(anyAttempt, anyExecution), release);
    }

    private static Expression Replace(
        LambdaExpression chain, ParameterExpression operation, ParameterExpression release) =>
        new ParameterReplacer(
            new Dictionary<ParameterExpression, Expression> { [chain.Parameters[0]] = operation, [chain.Parameters[1]] = release })
            .Visit(chain.Body);

    private static Expression AnyRelease(IDevalCopilotDbContext dbContext, ParameterExpression release, Expression body) =>
        AnyOf(dbContext, nameof(IDevalCopilotDbContext.LocalExecutionOwnershipReleases), release, body);

    /// <summary>`source.Any(parameter => body)` over one DbSet of the context, built as an expression so the chain body is inlined once.</summary>
    private static Expression AnyOf(IDevalCopilotDbContext dbContext, string setName, ParameterExpression parameter, Expression body)
    {
        var source = Expression.Property(Expression.Constant(dbContext, typeof(IDevalCopilotDbContext)), setName);
        var any = typeof(Queryable).GetMethods()
            .Single(method => method.Name == nameof(Queryable.Any) && method.GetParameters().Length == 2)
            .MakeGenericMethod(parameter.Type);
        return Expression.Call(any, source, Expression.Quote(Expression.Lambda(body, parameter)));
    }

    private sealed class ParameterReplacer(IReadOnlyDictionary<ParameterExpression, Expression> map) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => map.TryGetValue(node, out var replacement) ? replacement : node;
    }
}
