using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Application.Features.Runs.Policies.FormatRepair;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetChallengeResolutionAttemptStatus;

public sealed class GetChallengeResolutionAttemptStatusQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetChallengeResolutionAttemptStatusQuery, Result<ChallengeResolutionAttemptStatusQueryResult>>
{
    // Mirrors the adapter contract version fixed at claim time
    // (Attempt.ClaimAgentChallengeResolution) and the shared CodexProcessInvoker's own fixed
    // "--sandbox read-only" / "--ephemeral" arguments — a fixed CLI configuration fact, never a
    // provider-observed result and never invocation eligibility.
    private const string CodexResolverAdapterContractVersion = "codex-challenge-resolution-v1";
    private const string ConfiguredCodexCommandSandbox = "read-only";
    private const string ConfiguredCodexRolloutPersistence = "Disabled";

    public async Task<Result<ChallengeResolutionAttemptStatusQueryResult>> HandleAsync(
        GetChallengeResolutionAttemptStatusQuery query, CancellationToken cancellationToken)
    {
        var runExists = await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken);
        if (!runExists)
        {
            return Result<ChallengeResolutionAttemptStatusQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        Attempt? attempt;
        try
        {
            attempt = await dbContext.Attempts
                .AsNoTracking()
                .Where(candidate =>
                    candidate.RunId == query.RunId && candidate.Kind == AttemptKind.Agent && candidate.AgentRole == AgentRole.Resolver)
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
            return Result<ChallengeResolutionAttemptStatusQueryResult>.Success(ChallengeResolutionAttemptStatusQueryResult.NoAttempt);
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

        var orderedInputMessages = await dbContext.AttemptInputMessages
            .AsNoTracking()
            .Where(inputMessage => inputMessage.AttemptId == attempt.Id)
            .OrderBy(inputMessage => inputMessage.Sequence)
            .Select(inputMessage => new { inputMessage.Sequence, inputMessage.CollaborationMessageId })
            .ToListAsync(cancellationToken);

        var originalProposalMessageId = orderedInputMessages
            .Where(inputMessage => inputMessage.Sequence == 0)
            .Select(inputMessage => (Guid?)inputMessage.CollaborationMessageId)
            .SingleOrDefault();
        var challengeMessageIds = orderedInputMessages
            .Where(inputMessage => inputMessage.Sequence > 0)
            .Select(inputMessage => inputMessage.CollaborationMessageId)
            .ToArray();

        var isCoherentDefaultResolverAssignment =
            attempt.AgentRole == AgentRole.Resolver
            && assignment.Provider == AgentProvider.Codex
            && assignment.PermissionProfile == AgentPermissionProfile.ReadOnly
            && assignment.AdapterContractVersion == CodexResolverAdapterContractVersion;

        var configuredCommandSandbox = isCoherentDefaultResolverAssignment ? ConfiguredCodexCommandSandbox : null;
        var configuredRolloutPersistence = isCoherentDefaultResolverAssignment ? ConfiguredCodexRolloutPersistence : null;

        var repairLineage = await AgentRepairLineage.ReadForStatusAsync(dbContext, attempt, cancellationToken);

        return Result<ChallengeResolutionAttemptStatusQueryResult>.Success(new ChallengeResolutionAttemptStatusQueryResult(
            true,
            attempt.Id,
            attempt.AttemptNumber,
            originalProposalMessageId,
            challengeMessageIds,
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

    private static Result<ChallengeResolutionAttemptStatusQueryResult> InvalidAssignment() =>
        Result<ChallengeResolutionAttemptStatusQueryResult>.Failure(
            Error.Failure("agent_attempts.invalid_assignment", "Challenge resolution assignment metadata is unavailable."));
}
