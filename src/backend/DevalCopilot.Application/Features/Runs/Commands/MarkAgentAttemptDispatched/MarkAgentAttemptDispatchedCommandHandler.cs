using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;

/// <summary>
/// The authoritative last gate before the provider is ever invoked.
/// <c>GetEligibleAgentAttemptsQuery</c> is only a snapshot; workspace status, the mutation lease,
/// and the workspace's current checkpoint can all change during the pre-dispatch Git evidence
/// capture that runs between that snapshot and this call. This handler re-checks every one of
/// those facts in the same short transaction that records the dispatch marker, so nothing between
/// the snapshot and this commit can be dispatched on stale eligibility.
/// </summary>
public sealed class MarkAgentAttemptDispatchedCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<MarkAgentAttemptDispatchedCommand, Result<DateTimeOffset>>
{
    public const string WorkspaceNoLongerEligibleCode = "agent_attempts.workspace_no_longer_eligible";

    /// <summary>Distinct from <see cref="WorkspaceNoLongerEligibleCode"/> on purpose: the run,
    /// workspace, lease, and checkpoint all remain fully eligible here — only the reviewed
    /// Proposal's applicability was lost to a competing, already-completed critical review. A
    /// caller must never conflate the two; each maps to its own terminal
    /// <see cref="AgentOutcome"/> and its own dedicated recording command.</summary>
    public const string InputAlreadyReviewedCode = "agent_attempts.input_already_reviewed";

    /// <summary>The challenge-resolution counterpart to <see cref="InputAlreadyReviewedCode"/>,
    /// one level further down the collaboration protocol: the run, workspace, lease, and
    /// checkpoint remain fully eligible, but another challenge-resolution attempt already
    /// completed a successful resolution of the exact same ordered input set (the original
    /// Proposal plus its complete Challenge set) in the meantime.</summary>
    public const string InputAlreadyResolvedCode = "agent_attempts.input_already_resolved";

    /// <summary>The implementation counterpart to <see cref="InputAlreadyResolvedCode"/>: the
    /// run, workspace, lease, and checkpoint remain fully eligible, but another implementation
    /// attempt already completed a successful implementation of the exact same resolved plan
    /// against the exact same starting checkpoint in the meantime.</summary>
    public const string InputAlreadyImplementedCode = "agent_attempts.input_already_implemented";

    /// <summary>The code-review counterpart to <see cref="InputAlreadyResolvedCode"/>, one level
    /// further down the collaboration protocol: the run, workspace, lease, and checkpoint remain
    /// fully eligible, but another code-review attempt already completed a successful review of
    /// the exact same ExecutionReport-plus-verification-evidence input identity in the
    /// meantime.</summary>
    public const string InputAlreadyCodeReviewedCode = "agent_attempts.input_already_code_reviewed";

    public async Task<Result<DateTimeOffset>> HandleAsync(
        MarkAgentAttemptDispatchedCommand command, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (attempt is null || attempt.RunId != command.RunId)
        {
            return Result<DateTimeOffset>.Failure(
                Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Kind != AttemptKind.Agent)
        {
            return Result<DateTimeOffset>.Failure(
                Error.Conflict("attempts.not_agent", "The attempt is not an Agent attempt."));
        }

        if (attempt.Status != AttemptStatus.Running)
        {
            return Result<DateTimeOffset>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status} and cannot be dispatched."));
        }

        if (attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result<DateTimeOffset>.Failure(
                Error.Conflict("attempts.already_dispatched", "The attempt was already dispatched."));
        }

        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == attempt.RunId, cancellationToken);
        if (run is null || run.Lifecycle != RunLifecycle.Running)
        {
            return WorkspaceNoLongerEligible();
        }

        var workspace = await dbContext.GitWorkspaces
            .SingleOrDefaultAsync(candidate => candidate.Id == attempt.AgentGitWorkspaceId, cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return WorkspaceNoLongerEligible();
        }

        var leaseIsActive = await dbContext.RepositoryMutationLeases.AnyAsync(
            lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        if (!leaseIsActive)
        {
            return WorkspaceNoLongerEligible();
        }

        var currentCheckpointId = await dbContext.GitCheckpoints
            .Where(checkpoint => checkpoint.WorkspaceId == workspace.Id)
            .OrderByDescending(checkpoint => checkpoint.CheckpointNumber)
            .Select(checkpoint => (Guid?)checkpoint.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (currentCheckpointId != attempt.AgentGitCheckpointId)
        {
            return WorkspaceNoLongerEligible();
        }

        // The critical-review-specific half of the same authoritative last gate: the reviewed
        // Proposal's applicability can also have been lost between the eligibility snapshot and
        // this call — a competing critical review could have committed an Accepted/Challenged
        // result for the very same Proposal in that window. Codex planning attempts have no
        // input message to revalidate here.
        if (attempt.AgentRole == AgentRole.CriticalReviewer)
        {
            var inputMessageId = await dbContext.AttemptInputMessages
                .Where(inputMessage => inputMessage.AttemptId == attempt.Id && inputMessage.Sequence == 0)
                .Select(inputMessage => inputMessage.CollaborationMessageId)
                .SingleAsync(cancellationToken);

            var alreadyReviewed = await dbContext.Attempts
                .Join(
                    dbContext.AttemptInputMessages.Where(
                        inputMessage => inputMessage.CollaborationMessageId == inputMessageId && inputMessage.Sequence == 0),
                    candidate => candidate.Id,
                    inputMessage => inputMessage.AttemptId,
                    (candidate, inputMessage) => candidate)
                .AnyAsync(
                    candidate =>
                        candidate.Id != attempt.Id
                        && candidate.Kind == AttemptKind.Agent
                        && candidate.AgentProvider == AgentProvider.ClaudeCode
                        && candidate.AgentRole == AgentRole.CriticalReviewer
                        && (candidate.AgentOutcome == AgentOutcome.Accepted || candidate.AgentOutcome == AgentOutcome.Challenged),
                    cancellationToken);
            if (alreadyReviewed)
            {
                return Result<DateTimeOffset>.Failure(
                    Error.Conflict(
                        InputAlreadyReviewedCode,
                        "Another critical-review attempt already completed a successful review of this exact proposal."));
            }
        }

        // The challenge-resolution-specific half of the same authoritative last gate, mirroring
        // the CriticalReviewer branch above exactly: this attempt's own exact, ordered input
        // identity (the original Proposal plus its complete Challenge set) can also have lost
        // applicability between the eligibility snapshot and this call — a competing resolution
        // attempt could have committed a successful Resolved result for the very same ordered
        // input set in that window.
        if (attempt.AgentRole == AgentRole.Resolver)
        {
            var orderedInputMessageIds = await ChallengeResolutionInputIdentity.GetOrderedInputMessageIdsAsync(
                dbContext, attempt.Id, cancellationToken);
            var alreadyResolved = await ChallengeResolutionInputIdentity.HasCompetingExactResolutionAsync(
                dbContext, attempt.RunId, attempt.Id, orderedInputMessageIds, cancellationToken);
            if (alreadyResolved)
            {
                return Result<DateTimeOffset>.Failure(
                    Error.Conflict(
                        InputAlreadyResolvedCode,
                        "Another challenge-resolution attempt already completed a successful resolution of this exact input set."));
            }
        }

        // The implementation-specific half of the same authoritative last gate, mirroring the
        // Resolver branch above: this attempt's exact resolved-plan identity (its sequence-0
        // Proposal message, bound to its own starting checkpoint) can also have lost
        // applicability between the eligibility snapshot and this call — a competing
        // implementation attempt could have committed a successful Implemented result for the
        // very same plan and checkpoint in that window.
        if (attempt.AgentRole == AgentRole.Implementer)
        {
            var planProposalMessageId = await ImplementationInputIdentity.GetPlanProposalMessageIdAsync(dbContext, attempt.Id, cancellationToken);
            var alreadyImplemented = await ImplementationInputIdentity.HasCompetingSuccessfulImplementationAsync(
                dbContext, attempt.RunId, attempt.Id, planProposalMessageId, attempt.AgentGitCheckpointId!.Value, cancellationToken);
            if (alreadyImplemented)
            {
                return Result<DateTimeOffset>.Failure(
                    Error.Conflict(
                        InputAlreadyImplementedCode,
                        "Another implementation attempt already completed a successful implementation of this exact plan and checkpoint."));
            }
        }

        // The code-review-specific half of the same authoritative last gate, mirroring the
        // Resolver branch above exactly: this attempt's own exact input identity (the reviewed
        // ExecutionReport plus its exact ordered claimed verification-execution set) can also have
        // lost applicability between the eligibility snapshot and this call — a competing review
        // attempt could have committed a successful ReviewApproved/ReviewChangesRequested result
        // for the very same input identity in that window.
        if (attempt.AgentRole == AgentRole.CodeReviewer)
        {
            var executionReportMessageId = await CodeReviewInputIdentity.GetExecutionReportMessageIdAsync(dbContext, attempt.Id, cancellationToken);
            var orderedVerificationExecutionIds = await CodeReviewInputIdentity.GetOrderedVerificationExecutionIdsAsync(
                dbContext, attempt.Id, cancellationToken);
            var alreadyCodeReviewed = await CodeReviewInputIdentity.HasCompetingSuccessfulReviewAsync(
                dbContext, attempt.RunId, attempt.Id, executionReportMessageId, orderedVerificationExecutionIds, cancellationToken);
            if (alreadyCodeReviewed)
            {
                return Result<DateTimeOffset>.Failure(
                    Error.Conflict(
                        InputAlreadyCodeReviewedCode,
                        "Another code-review attempt already completed a successful review of this exact input identity."));
            }
        }

        var nowUtc = timeProvider.GetUtcNow();
        attempt.MarkAgentDispatched(nowUtc);

        return Result<DateTimeOffset>.Success(nowUtc);
    }

    private static Result<DateTimeOffset> WorkspaceNoLongerEligible() =>
        Result<DateTimeOffset>.Failure(
            Error.Conflict(
                WorkspaceNoLongerEligibleCode,
                "The workspace, its mutation lease, or its current checkpoint no longer permit dispatch."));
}
