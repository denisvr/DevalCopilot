using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Application.Features.Runs.Policies.FormatRepair;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCodeReviewAttemptStatus;

public sealed class GetCodeReviewAttemptStatusQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetCodeReviewAttemptStatusQuery, Result<CodeReviewAttemptStatusQueryResult>>
{
    // Mirrors the adapter contract version fixed at claim time (Attempt.ClaimAgentCodeReview) and
    // the shared CodexProcessInvoker's own fixed "--sandbox read-only" / "--ephemeral" arguments
    // — a fixed CLI configuration fact, never a provider-observed result and never invocation
    // eligibility.
    private const string CodexCodeReviewerAdapterContractVersion = "codex-implementation-review-v1";
    private const string ConfiguredCodexCommandSandbox = "read-only";
    private const string ConfiguredCodexRolloutPersistence = "Disabled";

    public async Task<Result<CodeReviewAttemptStatusQueryResult>> HandleAsync(
        GetCodeReviewAttemptStatusQuery query, CancellationToken cancellationToken)
    {
        var runExists = await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken);
        if (!runExists)
        {
            return Result<CodeReviewAttemptStatusQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        Attempt? attempt;
        try
        {
            attempt = await dbContext.Attempts
                .AsNoTracking()
                .Where(candidate =>
                    candidate.RunId == query.RunId && candidate.Kind == AttemptKind.Agent && candidate.AgentRole == AgentRole.CodeReviewer)
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
            return Result<CodeReviewAttemptStatusQueryResult>.Success(CodeReviewAttemptStatusQueryResult.NoAttempt);
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

        var executionReportMessageId = await dbContext.AttemptInputMessages
            .AsNoTracking()
            .Where(inputMessage => inputMessage.AttemptId == attempt.Id && inputMessage.Sequence == 0)
            .Select(inputMessage => (Guid?)inputMessage.CollaborationMessageId)
            .SingleOrDefaultAsync(cancellationToken);

        var isCoherentDefaultCodeReviewerAssignment =
            attempt.AgentRole == AgentRole.CodeReviewer
            && assignment.Provider == AgentProvider.Codex
            && assignment.PermissionProfile == AgentPermissionProfile.ReadOnly
            && assignment.AdapterContractVersion == CodexCodeReviewerAdapterContractVersion;

        var configuredCommandSandbox = isCoherentDefaultCodeReviewerAssignment ? ConfiguredCodexCommandSandbox : null;
        var configuredRolloutPersistence = isCoherentDefaultCodeReviewerAssignment ? ConfiguredCodexRolloutPersistence : null;

        var repairLineage = await AgentRepairLineage.ReadForStatusAsync(dbContext, attempt, cancellationToken);

        return Result<CodeReviewAttemptStatusQueryResult>.Success(new CodeReviewAttemptStatusQueryResult(
            true,
            attempt.Id,
            attempt.AttemptNumber,
            executionReportMessageId,
            attempt.Status,
            attempt.AgentOutcome,
            attempt.ClaimedAtUtc,
            attempt.AgentDispatchedAtUtc,
            attempt.CompletedAtUtc,
            artifacts,
            attempt.GetAgentProcessExecutionEvidence(),
            attempt.AgentTimeout,
            attempt.GetAgentTokenUsageEvidence(),
            configuredCommandSandbox,
            configuredRolloutPersistence,
            repairLineage.SourceAttemptId,
            repairLineage.SourceAttemptNumber));
    }

    private static Result<CodeReviewAttemptStatusQueryResult> InvalidAssignment() =>
        Result<CodeReviewAttemptStatusQueryResult>.Failure(
            Error.Failure("agent_attempts.invalid_assignment", "Code review assignment metadata is unavailable."));
}
