using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetReviewCorrectionAttemptStatus;

public sealed class GetReviewCorrectionAttemptStatusQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetReviewCorrectionAttemptStatusQuery, Result<ReviewCorrectionAttemptStatusQueryResult>>
{
    // Mirrors the adapter contract version fixed at claim time (Attempt.ClaimAgentReviewCorrection)
    // and the current ClaudeReviewCorrectionAdapter's own fixed CLI arguments — fixed
    // configuration facts, never a provider-observed result and never invocation eligibility.
    private const string ClaudeReviewCorrectionAdapterContractVersion = "claude-review-correction-v1";
    private const string ConfiguredClaudeReviewCorrectionPermissionMode = "acceptEdits";
    private const string ConfiguredClaudeReviewCorrectionSessionPersistence = "Disabled";
    private const string ConfiguredClaudeReviewCorrectionPermissionPrompts = "None";
    private const string ConfiguredClaudeReviewCorrectionResumeEligibility = "Ineligible";
    private const string ConfiguredClaudeReviewCorrectionBuiltInTools = "Read,Edit,Write,Glob,Grep";

    public async Task<Result<ReviewCorrectionAttemptStatusQueryResult>> HandleAsync(
        GetReviewCorrectionAttemptStatusQuery query, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking().SingleOrDefaultAsync(run => run.Id == query.RunId, cancellationToken);
        if (run is null)
        {
            return Result<ReviewCorrectionAttemptStatusQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        // Materialize the complete run-local lineage once. Eligibility is deliberately fail
        // closed, but it must not turn each historical candidate into another recursive EF graph
        // walk; the snapshot keeps this read-side query bounded by the run, not candidate count.
        // This load materializes every attempt on the run, including their AgentPermissionProfile
        // and AgentAdapterContractVersion columns, so an unparseable assignment enum on ANY
        // attempt in this run — not only the eventual review-correction candidate — throws here
        // during enum materialization. Caught below and reported as the same fail-closed
        // invalid_assignment result used elsewhere, without altering any lineage or budget
        // computation that follows: this run-wide fail-closed behavior is a broader guard than
        // the later per-attempt GetAssignmentSnapshot() check on the resolved correction attempt
        // itself, not a replacement for it.
        ImplementerExecutionReportEligibility.Snapshot snapshot;
        try
        {
            snapshot = await ImplementerExecutionReportEligibility.LoadSnapshotAsync(
                dbContext, query.RunId, cancellationToken);
        }
        catch (ArgumentException)
        {
            return InvalidAssignment();
        }
        catch (FormatException)
        {
            return InvalidAssignment();
        }
        catch (InvalidOperationException)
        {
            return InvalidAssignment();
        }

        var correctionAttemptsUsed = snapshot.AttemptsById.Values.Count(candidate =>
            candidate.RunId == query.RunId
            && candidate.Kind == AttemptKind.Agent
            && candidate.AgentResponseContract == AgentResponseContract.ReviewCorrection);

        var latestWorkspace = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        var currentCheckpointId = latestWorkspace is not null
            ? snapshot.CurrentCheckpointFor(latestWorkspace.Id)
            : null;
        var currentReview = latestWorkspace is { Status: WorkspaceStatus.Ready }
            && currentCheckpointId is { } resolvedCheckpointId
            ? ReviewCorrectionReviewEligibility.ResolveCurrent(
                snapshot, query.RunId, latestWorkspace.Id, resolvedCheckpointId)
            : null;

        IReadOnlyList<Guid> currentInputIds = currentReview is null
            ? []
            : [
                currentReview.ExecutionReport.Id,
                .. currentReview.OrderedFindings.Select(finding => finding.Id),
            ];
        var attempt = currentReview is null
            ? null
            : snapshot.AttemptsById.Values
                .Where(candidate => candidate.RunId == query.RunId
                    && candidate.Kind == AttemptKind.Agent
                    && candidate.AgentRole == AgentRole.Implementer
                    && candidate.AgentResponseContract == AgentResponseContract.ReviewCorrection
                    && HasExactInputs(snapshot.InputsFor(candidate.Id), currentInputIds))
                .OrderByDescending(candidate => candidate.AttemptNumber)
                .FirstOrDefault();
        ReviewCorrectionEscalation? latestEscalation = null;
        var hasAvailableAuthorization = false;
        if (currentReview is not null && correctionAttemptsUsed >= run.MaximumReviewCorrectionAttempts)
        {
            latestEscalation = await dbContext.ReviewCorrectionEscalations.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.RunId == query.RunId
                    && candidate.ImplementationReviewAttemptId == currentReview.ReviewAttempt.Id, cancellationToken);
            hasAvailableAuthorization = latestEscalation is not null && await dbContext.ReviewCorrectionAuthorizations.AsNoTracking()
                .AnyAsync(candidate => candidate.EscalationId == latestEscalation.Id && candidate.ConsumedByAttemptId == null, cancellationToken);
        }
        if (attempt is null)
        {
            var initialReportId = ResolveLatestInitialExecutionReportMessageId(snapshot, query.RunId);
            return Result<ReviewCorrectionAttemptStatusQueryResult>.Success(new ReviewCorrectionAttemptStatusQueryResult(
                false, null, null, currentReview?.ReviewAttempt.Id, initialReportId, null, null, null, null, 0, null, null, null, [],
                run.MaximumReviewCorrectionAttempts, correctionAttemptsUsed,
                correctionAttemptsUsed >= run.MaximumReviewCorrectionAttempts,
                latestEscalation?.Id, latestEscalation?.CollaborationMessageId, hasAvailableAuthorization));
        }

        // This assignment check is a pure addition after the lineage/budget computation above,
        // which is unaffected by it: it neither changes which attempt is resolved as "the current
        // correction attempt" nor how the budget/escalation facts are computed. It fails the whole
        // status closed when this exact resolved attempt's own persisted assignment metadata is
        // malformed. A corrupt assignment on some other, unrelated attempt on the run is not
        // caught here — that case already fails closed earlier, at the LoadSnapshotAsync call
        // above, during enum materialization of every attempt on the run.
        var assignment = attempt.GetAssignmentSnapshot();
        if (assignment is null)
        {
            return InvalidAssignment();
        }

        var reviewableExecutionReportMessageId = ResolveReviewableExecutionReportMessageId(snapshot, attempt);

        var artifacts = await dbContext.Artifacts.AsNoTracking()
            .Where(artifact => artifact.AttemptId == attempt.Id)
            .Select(artifact => new AgentCorrectionArtifactMetadata(
                artifact.Purpose, artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome))
            .ToArrayAsync(cancellationToken);
        var responseCount = await dbContext.CollaborationMessages.AsNoTracking()
            .CountAsync(message => message.AttemptId == attempt.Id && message.Type == CollaborationMessageType.RevisionResponse, cancellationToken);

        // attempt is already selected above by AgentResponseContract.ReviewCorrection (the
        // lineage query's own candidate filter); restating that contract here is a defensive,
        // explicit coherence check on the same already-guaranteed fact, never a change to which
        // attempt lineage resolution selects.
        var isCoherentDefaultReviewCorrectionAssignment =
            attempt.AgentRole == AgentRole.Implementer
            && attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection
            && assignment.Provider == AgentProvider.ClaudeCode
            && assignment.PermissionProfile == AgentPermissionProfile.WorkspaceEditOnly
            && assignment.AdapterContractVersion == ClaudeReviewCorrectionAdapterContractVersion;

        var configuredPermissionMode = isCoherentDefaultReviewCorrectionAssignment
            ? ConfiguredClaudeReviewCorrectionPermissionMode : null;
        var configuredSessionPersistence = isCoherentDefaultReviewCorrectionAssignment
            ? ConfiguredClaudeReviewCorrectionSessionPersistence : null;
        var configuredPermissionPrompts = isCoherentDefaultReviewCorrectionAssignment
            ? ConfiguredClaudeReviewCorrectionPermissionPrompts : null;
        var configuredResumeEligibility = isCoherentDefaultReviewCorrectionAssignment
            ? ConfiguredClaudeReviewCorrectionResumeEligibility : null;
        var configuredBuiltInTools = isCoherentDefaultReviewCorrectionAssignment
            ? ConfiguredClaudeReviewCorrectionBuiltInTools : null;

        return Result<ReviewCorrectionAttemptStatusQueryResult>.Success(new ReviewCorrectionAttemptStatusQueryResult(
            true, attempt.Id, attempt.AttemptNumber, currentReview?.ReviewAttempt.Id, reviewableExecutionReportMessageId, attempt.Status, attempt.AgentOutcome,
            attempt.AgentGitCheckpointId, attempt.AgentResultGitCheckpointId, responseCount,
            attempt.ClaimedAtUtc, attempt.AgentDispatchedAtUtc, attempt.CompletedAtUtc, artifacts,
            run.MaximumReviewCorrectionAttempts, correctionAttemptsUsed,
            correctionAttemptsUsed >= run.MaximumReviewCorrectionAttempts,
            latestEscalation?.Id, latestEscalation?.CollaborationMessageId, hasAvailableAuthorization,
            attempt.GetAgentProcessExecutionEvidence(), attempt.AgentTimeout, attempt.GetAgentTokenUsageEvidence(),
            configuredPermissionMode, configuredSessionPersistence, configuredPermissionPrompts,
            configuredResumeEligibility, configuredBuiltInTools));
    }

    private static Result<ReviewCorrectionAttemptStatusQueryResult> InvalidAssignment() =>
        Result<ReviewCorrectionAttemptStatusQueryResult>.Failure(
            Error.Failure("agent_attempts.invalid_assignment", "Review correction assignment metadata is unavailable."));

    private static bool HasExactInputs(
        IReadOnlyList<AttemptInputMessage> inputs,
        IReadOnlyList<Guid> expectedMessageIds)
    {
        var ordered = inputs.OrderBy(input => input.Sequence).ToArray();
        return ordered.Length == expectedMessageIds.Count
            && ordered.Select((input, index) => input.Sequence == index
                && input.CollaborationMessageId == expectedMessageIds[index]).All(isMatch => isMatch);
    }

    private static Guid? ResolveReviewableExecutionReportMessageId(
        ImplementerExecutionReportEligibility.Snapshot snapshot, Attempt latestCorrectionAttempt)
    {
        if (latestCorrectionAttempt.Status != AttemptStatus.Completed)
        {
            return null;
        }

        if (latestCorrectionAttempt.AgentOutcome == AgentOutcome.CorrectionApplied)
        {
            return ResolveReportOwnedByAttempt(snapshot, latestCorrectionAttempt);
        }

        if (latestCorrectionAttempt.AgentOutcome == AgentOutcome.InputAlreadyCorrected)
        {
            var inputIdentity = snapshot.InputsFor(latestCorrectionAttempt.Id)
                .Select(input => input.CollaborationMessageId)
                .ToArray();
            var candidates = snapshot.AttemptsById.Values
                .Where(candidate => candidate.Id != latestCorrectionAttempt.Id
                    && candidate.RunId == latestCorrectionAttempt.RunId
                    && candidate.Kind == AttemptKind.Agent
                    && candidate.AgentRole == AgentRole.Implementer
                    && candidate.AgentResponseContract == AgentResponseContract.ReviewCorrection
                    && candidate.Status == AttemptStatus.Completed
                    && candidate.AgentOutcome == AgentOutcome.CorrectionApplied
                    && candidate.AgentGitCheckpointId == latestCorrectionAttempt.AgentGitCheckpointId)
                .OrderByDescending(candidate => candidate.AttemptNumber)
                .ToArray();

            var matchingReports = new List<Guid>();
            foreach (var candidate in candidates)
            {
                var candidateInputs = snapshot.InputsFor(candidate.Id)
                    .Select(input => input.CollaborationMessageId)
                    .ToArray();
                if (candidateInputs.SequenceEqual(inputIdentity)
                    && ResolveReportOwnedByAttempt(snapshot, candidate) is { } reportId)
                {
                    matchingReports.Add(reportId);
                }
            }

            return matchingReports.Count == 1 ? matchingReports[0] : null;
        }

        return null;
    }

    private static Guid? ResolveReportOwnedByAttempt(
        ImplementerExecutionReportEligibility.Snapshot snapshot, Attempt attempt)
    {
        if (attempt.AgentGitWorkspaceId is not { } workspaceId
            || attempt.AgentResultGitCheckpointId is not { } resultCheckpointId)
        {
            return null;
        }

        if (snapshot.CurrentCheckpointFor(workspaceId) != resultCheckpointId)
        {
            return null;
        }

        var reports = snapshot.Messages
            .Where(message => message.AttemptId == attempt.Id
                && message.Type == CollaborationMessageType.ExecutionReport
                && message.Provenance == CollaborationMessageProvenance.ProviderObserved)
            .OrderBy(message => message.Sequence)
            .ToArray();
        if (reports.Length != 1)
        {
            return null;
        }

        var validation = ImplementerExecutionReportEligibility.Resolve(
            snapshot, reports[0], attempt.RunId, workspaceId, resultCheckpointId);
        return validation?.ExecutionReport.Id;
    }

    private static Guid? ResolveLatestInitialExecutionReportMessageId(
        ImplementerExecutionReportEligibility.Snapshot snapshot, Guid runId)
    {
        var candidates = snapshot.AttemptsById.Values
            .Where(candidate => candidate.RunId == runId
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentRole == AgentRole.Implementer
                && candidate.AgentResponseContract == AgentResponseContract.ImplementationReport
                && candidate.Status == AttemptStatus.Completed
                && candidate.AgentOutcome == AgentOutcome.Implemented)
            .OrderByDescending(candidate => candidate.AttemptNumber)
            .ToArray();

        foreach (var candidate in candidates)
        {
            if (ResolveReportOwnedByAttempt(snapshot, candidate) is { } reportId)
            {
                return reportId;
            }
        }

        return null;
    }
}
