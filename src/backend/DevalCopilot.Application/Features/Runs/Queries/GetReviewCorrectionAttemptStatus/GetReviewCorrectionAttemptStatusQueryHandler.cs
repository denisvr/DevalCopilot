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
        var snapshot = await ImplementerExecutionReportEligibility.LoadSnapshotAsync(
            dbContext, query.RunId, cancellationToken);
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

        var reviewableExecutionReportMessageId = ResolveReviewableExecutionReportMessageId(snapshot, attempt);

        var artifacts = await dbContext.Artifacts.AsNoTracking()
            .Where(artifact => artifact.AttemptId == attempt.Id)
            .Select(artifact => new AgentCorrectionArtifactMetadata(
                artifact.Purpose, artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome))
            .ToArrayAsync(cancellationToken);
        var responseCount = await dbContext.CollaborationMessages.AsNoTracking()
            .CountAsync(message => message.AttemptId == attempt.Id && message.Type == CollaborationMessageType.RevisionResponse, cancellationToken);

        return Result<ReviewCorrectionAttemptStatusQueryResult>.Success(new ReviewCorrectionAttemptStatusQueryResult(
            true, attempt.Id, attempt.AttemptNumber, currentReview?.ReviewAttempt.Id, reviewableExecutionReportMessageId, attempt.Status, attempt.AgentOutcome,
            attempt.AgentGitCheckpointId, attempt.AgentResultGitCheckpointId, responseCount,
            attempt.ClaimedAtUtc, attempt.AgentDispatchedAtUtc, attempt.CompletedAtUtc, artifacts,
            run.MaximumReviewCorrectionAttempts, correctionAttemptsUsed,
            correctionAttemptsUsed >= run.MaximumReviewCorrectionAttempts,
            latestEscalation?.Id, latestEscalation?.CollaborationMessageId, hasAvailableAuthorization,
            attempt.GetAgentProcessExecutionEvidence(), attempt.AgentTimeout, attempt.GetAgentTokenUsageEvidence()));
    }

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
