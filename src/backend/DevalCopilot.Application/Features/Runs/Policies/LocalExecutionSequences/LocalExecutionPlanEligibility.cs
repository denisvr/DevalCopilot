using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// Whether one selected Proposal is an implementable plan for a new local execution sequence (ADR-0060). It reuses the existing lineage
/// evidence and is deliberately stricter than an ordinary manual implementation claim: it admits only an explicitly Accepted Planner
/// root, an explicitly Accepted depth-one revision with its exact Acceptance, or ADR-0016's separately Human-authorized final
/// depth-two revision with one unconsumed, current, coherent grant. An unreviewed first revision is insufficient. Nothing here
/// manufactures an Acceptance or a grant. The ordered input identities it returns are exactly the ones an ordinary implementation claim
/// would record, so the claim seam can compare them.
/// </summary>
internal static class LocalExecutionPlanEligibility
{
    internal sealed record Eligible(
        LocalExecutionPlanAuthority Authority, IReadOnlyList<Guid> OrderedInputMessageIds, Guid? GrantId);

    public static async Task<(Eligible? Plan, Error? Error)> EvaluateAsync(
        IDevalCopilotDbContext dbContext,
        Run run,
        Guid planProposalMessageId,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            return await EvaluateCoreAsync(dbContext, run, planProposalMessageId, workspace, checkpoint, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // A persisted row of the plan's lineage could not be materialized: a fixed refusal, never a stored value.
            return (null, LocalExecutionSequenceErrors.PlanNotEligible());
        }
    }

    private static async Task<(Eligible? Plan, Error? Error)> EvaluateCoreAsync(
        IDevalCopilotDbContext dbContext,
        Run run,
        Guid planProposalMessageId,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var proposal = await dbContext.CollaborationMessages.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == planProposalMessageId, cancellationToken);
        if (proposal is null || proposal.RunId != run.Id)
        {
            return (null, LocalExecutionSequenceErrors.PlanNotFound());
        }

        if (proposal.Type != CollaborationMessageType.Proposal || proposal.AttemptId is null)
        {
            return (null, LocalExecutionSequenceErrors.PlanNotEligible());
        }

        var owner = await dbContext.Attempts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == proposal.AttemptId, cancellationToken);
        if (owner is null
            || owner.RunId != run.Id
            || owner.Kind != AttemptKind.Agent
            || !AgentAttemptIdentity.IsCoherent(owner)
            || owner.Status != AttemptStatus.Completed
            || proposal.Provenance != CollaborationMessageProvenance.ProviderObserved
            || owner.AgentProvider is not { } provider
            || !Enum.IsDefined(provider)
            || proposal.Actor != ParticipantIdentity.ForAgent(owner.AgentRole!.Value, provider)
            || owner.AgentGitWorkspaceId != workspace.Id
            || owner.AgentGitCheckpointId != checkpoint.Id
            || !string.Equals(owner.AgentCheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return (null, LocalExecutionSequenceErrors.PlanNotEligible());
        }

        (Eligible? Plan, Error? Error) result;
        if (owner.AgentRole == AgentRole.Planner
            && owner.AgentResponseContract == AgentResponseContract.Proposal
            && owner.AgentOutcome == AgentOutcome.Proposed)
        {
            result = await EvaluateRootAsync(dbContext, run, proposal, workspace, checkpoint, cancellationToken);
        }
        else if (owner.AgentRole == AgentRole.Resolver
            && owner.AgentResponseContract == AgentResponseContract.ChallengeResolution
            && owner.AgentOutcome == AgentOutcome.Resolved)
        {
            result = await EvaluateRevisionAsync(dbContext, run, proposal, workspace, checkpoint, cancellationToken);
        }
        else
        {
            return (null, LocalExecutionSequenceErrors.PlanNotEligible());
        }

        if (result.Plan is null)
        {
            return result;
        }

        return await ImplementationInputIdentity.HasCompetingSuccessfulImplementationAsync(
            dbContext, run.Id, Guid.Empty, planProposalMessageId, checkpoint.Id, cancellationToken)
            ? (null, LocalExecutionSequenceErrors.PlanNotEligible())
            : result;
    }

    private static async Task<(Eligible? Plan, Error? Error)> EvaluateRootAsync(
        IDevalCopilotDbContext dbContext,
        Run run,
        CollaborationMessage proposal,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => candidate.RunId == run.Id
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentRole == AgentRole.CriticalReviewer
                && candidate.Status == AttemptStatus.Completed
                && candidate.AgentOutcome == AgentOutcome.Accepted)
            .Join(
                dbContext.AttemptInputMessages.AsNoTracking()
                    .Where(input => input.Sequence == 0 && input.CollaborationMessageId == proposal.Id),
                candidate => candidate.Id,
                input => input.AttemptId,
                (candidate, _) => candidate)
            .OrderBy(candidate => candidate.AttemptNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (review is null
            || review.AgentProvider is not { } reviewProvider
            || !Enum.IsDefined(reviewProvider)
            || !AgentAttemptIdentity.IsCoherent(review)
            || review.AgentGitWorkspaceId != workspace.Id
            || review.AgentGitCheckpointId != checkpoint.Id
            || !string.Equals(review.AgentCheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return (null, LocalExecutionSequenceErrors.PlanNotEligible());
        }

        var expectedActor = ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, reviewProvider);
        var acceptance = await dbContext.CollaborationMessages.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.AttemptId == review.Id
                && candidate.Type == CollaborationMessageType.Acceptance
                && candidate.InReplyToMessageId == proposal.Id,
            cancellationToken);
        return acceptance is null
            || acceptance.Provenance != CollaborationMessageProvenance.ProviderObserved
            || acceptance.Actor != expectedActor
                ? (null, LocalExecutionSequenceErrors.PlanNotEligible())
                : (new Eligible(LocalExecutionPlanAuthority.AcceptedRoot, [proposal.Id, acceptance.Id], null), null);
    }

    private static async Task<(Eligible? Plan, Error? Error)> EvaluateRevisionAsync(
        IDevalCopilotDbContext dbContext,
        Run run,
        CollaborationMessage proposal,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var snapshot = await PlanningLineage.TryLoadSnapshotAsync(dbContext, run.Id, cancellationToken);
        if (snapshot is null)
        {
            return (null, LocalExecutionSequenceErrors.PlanNotEligible());
        }

        var evaluation = PlanningLineage.Evaluate(
            snapshot, run.Id, workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, proposal.Id);
        if (evaluation.Node is not { } node || !PlanningLineage.OwnsExactlyOneProposal(snapshot, node.Root.Owner))
        {
            return (null, LocalExecutionSequenceErrors.PlanNotEligible());
        }

        if (node.Depth > PlanningLineage.MaximumImplementableDepth)
        {
            return node.Depth == PlanningLineage.MaximumDepth
                ? EvaluateHumanAuthorization(snapshot, node, workspace, checkpoint)
                : (null, LocalExecutionSequenceErrors.PlanNotEligible());
        }

        // Stricter than an ordinary claim: exactly one Accepted review with its exact Acceptance. An unreviewed first revision is refused.
        var reviews = PlanningLineage.SuccessfulReviewsOf(snapshot, run.Id, node.Proposal.Id);
        if (reviews.Count != 1 || reviews[0].AgentOutcome != AgentOutcome.Accepted)
        {
            return (null, LocalExecutionSequenceErrors.PlanNotEligible());
        }

        var acceptance = PlanningLineage.FindExactAcceptance(
            snapshot, reviews[0], run.Id, node.Proposal.Id, workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256);
        if (acceptance is null)
        {
            return (null, LocalExecutionSequenceErrors.PlanNotEligible());
        }

        var inputs = new List<Guid> { proposal.Id };
        inputs.AddRange(node.Decisions.Select(decision => decision.Id));
        inputs.Add(acceptance.Id);
        return (new Eligible(LocalExecutionPlanAuthority.AcceptedRevision, inputs, null), null);
    }

    private static (Eligible? Plan, Error? Error) EvaluateHumanAuthorization(
        ImplementerExecutionReportEligibility.Snapshot snapshot, PlanningLineage.Node node, GitWorkspace workspace, GitCheckpoint checkpoint)
    {
        var grants = snapshot.PlanningAuthorizations.Where(candidate => candidate.FinalProposalMessageId == node.Proposal.Id).ToArray();
        if (grants.Length != 1)
        {
            return (null, LocalExecutionSequenceErrors.PlanNotEligible());
        }

        var grant = grants[0];
        if (!PlanningImplementationAuthorizationEvidence.IsBoundTo(grant, workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256)
            || PlanningImplementationAuthorizationEvidence.IsSuperseded(snapshot, node)
            || PlanningImplementationAuthorizationEvidence.ValidateRecorded(snapshot, grant) is not { } recorded
            || recorded.Final.Proposal.Id != node.Proposal.Id
            || PlanningLineage.SuccessfulReviewsOf(snapshot, grant.RunId, node.Proposal.Id).Count != 0
            || !grant.IsAvailable)
        {
            return (null, LocalExecutionSequenceErrors.PlanNotEligible());
        }

        return (new Eligible(LocalExecutionPlanAuthority.HumanAuthorizedRevision, recorded.ExpectedInputMessageIds, grant.Id), null);
    }
}
