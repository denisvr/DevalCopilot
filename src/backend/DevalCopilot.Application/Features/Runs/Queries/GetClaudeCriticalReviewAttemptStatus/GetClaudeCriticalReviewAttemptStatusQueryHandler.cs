using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Application.Features.Runs.Policies.FormatRepair;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetClaudeCriticalReviewAttemptStatus;

public sealed class GetClaudeCriticalReviewAttemptStatusQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetClaudeCriticalReviewAttemptStatusQuery, Result<ClaudeCriticalReviewAttemptStatusQueryResult>>
{
    // Mirrors the adapter contract version fixed at claim time (Attempt.ClaimAgentCriticalReview)
    // and the current ClaudeCriticalReviewAdapter's own fixed CLI arguments — fixed configuration
    // facts, never a provider-observed result and never invocation eligibility.
    private const string ClaudeCriticalReviewerAdapterContractVersion = "claude-critical-review-v1";
    private const string ConfiguredClaudeCriticalReviewerPermissionMode = "plan";
    private const string ConfiguredClaudeCriticalReviewerSessionPersistence = "Disabled";
    private const string ConfiguredClaudeCriticalReviewerPermissionPrompts = "None";
    private const string ConfiguredClaudeCriticalReviewerResumeEligibility = "Ineligible";
    private const string ConfiguredClaudeCriticalReviewerBuiltInTools = "None";

    public async Task<Result<ClaudeCriticalReviewAttemptStatusQueryResult>> HandleAsync(
        GetClaudeCriticalReviewAttemptStatusQuery query, CancellationToken cancellationToken)
    {
        var runExists = await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken);
        if (!runExists)
        {
            return Result<ClaudeCriticalReviewAttemptStatusQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        Attempt? attempt;
        try
        {
            attempt = await dbContext.Attempts
                .AsNoTracking()
                .Where(candidate =>
                    candidate.RunId == query.RunId && candidate.Kind == AttemptKind.Agent && candidate.AgentRole == AgentRole.CriticalReviewer)
                .OrderByDescending(candidate => candidate.AttemptNumber)
                .FirstOrDefaultAsync(cancellationToken);
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

        if (attempt is null)
        {
            return Result<ClaudeCriticalReviewAttemptStatusQueryResult>.Success(ClaudeCriticalReviewAttemptStatusQueryResult.NoAttempt);
        }

        var assignment = attempt.GetAssignmentSnapshot();
        if (assignment is null)
        {
            return InvalidAssignment();
        }

        var artifacts = await dbContext.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.AttemptId == attempt.Id)
            .Select(artifact => new AgentAttemptArtifactMetadata(artifact.Purpose, artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome))
            .ToArrayAsync(cancellationToken);

        var reviewedProposalMessageId = await dbContext.AttemptInputMessages
            .AsNoTracking()
            .Where(inputMessage => inputMessage.AttemptId == attempt.Id && inputMessage.Sequence == 0)
            .Select(inputMessage => (Guid?)inputMessage.CollaborationMessageId)
            .SingleOrDefaultAsync(cancellationToken);

        var isCoherentDefaultCriticalReviewerAssignment =
            attempt.AgentRole == AgentRole.CriticalReviewer
            && attempt.AgentResponseContract == AgentResponseContract.CriticalReview
            && assignment.Provider == AgentProvider.ClaudeCode
            && assignment.PermissionProfile == AgentPermissionProfile.ReadOnly
            && assignment.AdapterContractVersion == ClaudeCriticalReviewerAdapterContractVersion;

        var configuredPermissionMode = isCoherentDefaultCriticalReviewerAssignment
            ? ConfiguredClaudeCriticalReviewerPermissionMode : null;
        var configuredSessionPersistence = isCoherentDefaultCriticalReviewerAssignment
            ? ConfiguredClaudeCriticalReviewerSessionPersistence : null;
        var configuredPermissionPrompts = isCoherentDefaultCriticalReviewerAssignment
            ? ConfiguredClaudeCriticalReviewerPermissionPrompts : null;
        var configuredResumeEligibility = isCoherentDefaultCriticalReviewerAssignment
            ? ConfiguredClaudeCriticalReviewerResumeEligibility : null;
        var configuredBuiltInTools = isCoherentDefaultCriticalReviewerAssignment
            ? ConfiguredClaudeCriticalReviewerBuiltInTools : null;

        var repairLineage = await AgentRepairLineage.ReadForStatusAsync(dbContext, attempt, cancellationToken);

        return Result<ClaudeCriticalReviewAttemptStatusQueryResult>.Success(new ClaudeCriticalReviewAttemptStatusQueryResult(
            true,
            attempt.Id,
            attempt.AttemptNumber,
            reviewedProposalMessageId,
            attempt.Status,
            attempt.AgentOutcome,
            attempt.ClaimedAtUtc,
            attempt.AgentDispatchedAtUtc,
            attempt.CompletedAtUtc,
            artifacts,
            attempt.GetAgentProcessExecutionEvidence(),
            attempt.AgentTimeout,
            attempt.GetAgentTokenUsageEvidence(),
            configuredPermissionMode,
            configuredSessionPersistence,
            configuredPermissionPrompts,
            configuredResumeEligibility,
            configuredBuiltInTools,
            repairLineage.SourceAttemptId,
            repairLineage.SourceAttemptNumber));
    }

    private static Result<ClaudeCriticalReviewAttemptStatusQueryResult> InvalidAssignment() =>
        Result<ClaudeCriticalReviewAttemptStatusQueryResult>.Failure(
            Error.Failure("agent_attempts.invalid_assignment", "Critical review assignment metadata is unavailable."));
}
