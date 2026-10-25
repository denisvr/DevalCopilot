using System.Linq.Expressions;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The one shared definition of an unresolved execution owner, extended narrowly by ADR-0060 (superseding only the owner classification
/// of ADR-0057 and ADR-0058): a row is an owner while its status is not a recognized terminal one, and also when it is linked to a local
/// execution sequence, was dispatched and lacks coherent host-recorded process Exited evidence, even if a legacy result path recorded a
/// recognized terminal status. A timeout, cancellation, adapter exception, missing evidence or a terminal enum alone proves no exit, and
/// a recognized terminal result stays available as truthful failure evidence while its exact operation stays quarantined. Each ordinary
/// row is counted once because the extension is part of one predicate, not a second row set. Unlinked historical rows are unchanged.
/// ADR-0065 adds exactly one alternative: a recognized terminal retained operation is no longer an owner when ONE complete coherent
/// Human release chain exists for it (<see cref="LocalExecutionReleaseChain"/>); a receipt, an observation or an event alone never is.
///
/// <para>
/// Every predicate is database-side, so a stored status this build does not recognize is never materialized as finished. An Agent proof
/// is the validated process-evidence tuple (dispatched, Exited, an exit code, a non-negative duration); a verification proof is Exited
/// and an exit code in its own result. Nothing here proves that every descendant stopped, and a release changes no outcome.
/// </para>
/// </summary>
internal static class LocalExecutionOwnership
{
    /// <summary>The Agent attempt is an unresolved owner: not terminal, or linked to a sequence, dispatched and without Exited proof
    /// (unless one coherent Human release chain exists, ADR-0065).</summary>
    public static Expression<Func<Attempt, bool>> UnresolvedAttempt(IDevalCopilotDbContext dbContext)
    {
        Expression<Func<Attempt, bool>> nonterminal = attempt =>
            attempt.Status != AttemptStatus.Completed
            && attempt.Status != AttemptStatus.Failed
            && attempt.Status != AttemptStatus.Interrupted;
        Expression<Func<Attempt, bool>> retained = attempt =>
            attempt.Kind == AttemptKind.Agent
            && attempt.AgentDispatchedAtUtc != null
            && !(attempt.AgentProcessOutcome == ProcessOutcome.Exited
                && attempt.AgentProcessExitCode != null
                && attempt.AgentProcessDuration != null
                && attempt.AgentProcessDuration >= TimeSpan.Zero)
            && dbContext.LocalExecutionSequenceSteps.Any(
                step => step.OperationId == attempt.Id && step.Kind != LocalExecutionStepKind.Verification);
        return Or(nonterminal, AndNot(retained, LocalExecutionReleaseChain.AgentReleased(dbContext)));
    }

    /// <summary>The verification execution is an unresolved owner: not a recognized terminal status (the dispatch refusal is one), or
    /// linked to a sequence, dispatched and without an Exited outcome and exit code (unless one coherent Human release chain exists,
    /// ADR-0065).</summary>
    public static Expression<Func<VerificationExecution, bool>> UnresolvedExecution(IDevalCopilotDbContext dbContext)
    {
        Expression<Func<VerificationExecution, bool>> nonterminal = execution =>
            execution.Status != VerificationExecutionStatus.Passed
            && execution.Status != VerificationExecutionStatus.Failed
            && execution.Status != VerificationExecutionStatus.TimedOut
            && execution.Status != VerificationExecutionStatus.Cancelled
            && execution.Status != VerificationExecutionStatus.Interrupted
            && execution.Status != VerificationExecutionStatus.SourceChanged
            && execution.Status != VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused;
        Expression<Func<VerificationExecution, bool>> retained = execution =>
            execution.DispatchedAtUtc != null
            && !(execution.Outcome == VerificationExecutionOutcome.Exited && execution.ExitCode != null)
            && dbContext.LocalExecutionSequenceSteps.Any(
                step => step.OperationId == execution.Id && step.Kind == LocalExecutionStepKind.Verification);
        return Or(nonterminal, AndNot(retained, LocalExecutionReleaseChain.ExecutionReleased(dbContext)));
    }

    private static Expression<Func<T, bool>> Or<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right) =>
        Combine(left, right, Expression.OrElse);

    private static Expression<Func<T, bool>> AndNot<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right) =>
        Combine(left, right, (first, second) => Expression.AndAlso(first, Expression.Not(second)));

    private static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>> left, Expression<Func<T, bool>> right, Func<Expression, Expression, Expression> join)
    {
        var parameter = left.Parameters[0];
        var body = join(left.Body, new ParameterSubstitution(right.Parameters[0], parameter).Visit(right.Body));
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private sealed class ParameterSubstitution(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }

    /// <summary>
    /// Whether any linked step of the scope names an operation whose row is missing, foreign (another run, project, workspace or
    /// source), of the wrong contract or inconsistent with its link. Such evidence is never a zero count: the shared policies treat it
    /// as Unavailable. <paramref name="projectId"/> scopes the check to one project, or null for every sequence.
    /// </summary>
    public static async Task<bool> HasIncoherentLinkAsync(
        IDevalCopilotDbContext dbContext, Guid? projectId, CancellationToken cancellationToken)
    {
        // ADR-0062: a held pre-claim reservation whose step, sequence or workspace identity is incomplete is never a zero count either.
        if (await LocalExecutionReservationOwnership.HasIncoherentReservationAsync(dbContext, projectId, cancellationToken))
        {
            return true;
        }

        // ADR-0065: a release that is not one complete coherent chain proves nothing and is never a zero count either; the operation it
        // names keeps owning.
        if (await LocalExecutionReleaseChain.HasMalformedReleaseAsync(dbContext, projectId, cancellationToken))
        {
            return true;
        }

        var sequences = dbContext.LocalExecutionSequences.AsNoTracking();
        if (projectId is { } project)
        {
            sequences = sequences.Where(sequence => sequence.ProjectId == project);
        }

        var steps = dbContext.LocalExecutionSequenceSteps.AsNoTracking()
            .Where(step => step.OperationId != null && sequences.Any(sequence => sequence.Id == step.SequenceId));

        var agentSteps = steps.Where(step => step.Kind != LocalExecutionStepKind.Verification);
        var coherentAgent = await agentSteps.CountAsync(
            step => dbContext.Attempts.Any(attempt => attempt.Id == step.OperationId
                && attempt.Kind == AttemptKind.Agent
                && attempt.AgentGitCheckpointId == step.SourceCheckpointId
                && dbContext.LocalExecutionSequences.Any(sequence => sequence.Id == step.SequenceId
                    && attempt.RunId == sequence.RunId
                    && attempt.AgentGitWorkspaceId == sequence.WorkspaceId)
                && (step.Kind == LocalExecutionStepKind.Implementation
                        ? attempt.AgentResponseContract == AgentResponseContract.ImplementationReport
                        : step.Kind == LocalExecutionStepKind.CodeReview
                            ? attempt.AgentResponseContract == AgentResponseContract.ImplementationReview
                            : step.Kind == LocalExecutionStepKind.VerificationDiagnosis
                                ? attempt.AgentResponseContract == AgentResponseContract.VerificationDiagnosis
                                : attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection)),
            cancellationToken);
        if (coherentAgent != await agentSteps.CountAsync(cancellationToken))
        {
            return true;
        }

        var verificationSteps = steps.Where(step => step.Kind == LocalExecutionStepKind.Verification);
        var coherentVerification = await verificationSteps.CountAsync(
            step => dbContext.VerificationExecutions.Any(execution => execution.Id == step.OperationId
                && execution.GitCheckpointId == step.SourceCheckpointId
                && dbContext.LocalExecutionSequences.Any(sequence => sequence.Id == step.SequenceId
                    && execution.ProjectId == sequence.ProjectId
                    && execution.GitWorkspaceId == sequence.WorkspaceId)),
            cancellationToken);
        return coherentVerification != await verificationSteps.CountAsync(cancellationToken);
    }
}
