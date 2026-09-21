using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>Resolves the one currently applicable implementation review for a run.</summary>
internal static class ReviewCorrectionReviewEligibility
{
    internal sealed record CurrentReview(
        Attempt ReviewAttempt,
        CollaborationMessage ExecutionReport,
        IReadOnlyList<CollaborationMessage> OrderedFindings,
        ImplementerExecutionReportEligibility.Result ImplementationLineage);

    internal static CurrentReview? ResolveCurrent(
        ImplementerExecutionReportEligibility.Snapshot snapshot,
        Guid runId,
        Guid workspaceId,
        Guid checkpointId)
    {
        var latestReview = snapshot.AttemptsById.Values
            .Where(candidate => candidate.RunId == runId
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentRole == AgentRole.CodeReviewer
                && candidate.AgentResponseContract == AgentResponseContract.ImplementationReview
                && candidate.AgentOutcome == AgentOutcome.ReviewChangesRequested
                && candidate.Status == AttemptStatus.Completed
                && candidate.AgentGitWorkspaceId == workspaceId
                && candidate.AgentGitCheckpointId == checkpointId)
            .OrderByDescending(candidate => candidate.AttemptNumber)
            .FirstOrDefault();

        return latestReview is null
            ? null
            : ResolveForAttempt(snapshot, latestReview, runId, workspaceId, checkpointId);
    }

    internal static CurrentReview? ResolveForAttempt(
        ImplementerExecutionReportEligibility.Snapshot snapshot,
        Attempt review,
        Guid runId,
        Guid workspaceId,
        Guid checkpointId)
    {
        if (review.RunId != runId
            || review.Kind != AttemptKind.Agent
            || review.AgentRole != AgentRole.CodeReviewer
            || review.AgentResponseContract != AgentResponseContract.ImplementationReview
            || review.AgentOutcome != AgentOutcome.ReviewChangesRequested
            || review.Status != AttemptStatus.Completed
            || review.AgentGitWorkspaceId != workspaceId
            || review.AgentGitCheckpointId != checkpointId
            || review.AgentProvider is not { } provider
            || !Enum.IsDefined(provider))
        {
            return null;
        }

        var reviewInputs = snapshot.InputsFor(review.Id);
        if (reviewInputs.Count != 1 || reviewInputs[0].Sequence != 0
            || !snapshot.MessagesById.TryGetValue(reviewInputs[0].CollaborationMessageId, out var executionReport)
            || executionReport.Type != CollaborationMessageType.ExecutionReport
            || executionReport.RunId != runId)
        {
            return null;
        }

        var implementationLineage = ImplementerExecutionReportEligibility.Resolve(
            snapshot, executionReport, runId, workspaceId, checkpointId);
        if (implementationLineage is null)
        {
            return null;
        }

        var reviewFindings = snapshot.Messages
            .Where(message => message.RunId == runId
                && message.AttemptId == review.Id
                && message.Type == CollaborationMessageType.ReviewFinding)
            .OrderBy(message => message.Sequence)
            .ToArray();
        var expectedActor = ParticipantIdentity.ForAgent(AgentRole.CodeReviewer, provider);
        if (reviewFindings.Length is 0 or > ReviewCorrectionOutputSchema.MaximumFindings
            || reviewFindings.Any(finding => finding.Provenance != CollaborationMessageProvenance.ProviderObserved
                || finding.Actor != expectedActor
                || finding.InReplyToMessageId != executionReport.Id
                || finding.Sequence <= executionReport.Sequence))
        {
            return null;
        }

        return new CurrentReview(review, executionReport, reviewFindings, implementationLineage);
    }
}
