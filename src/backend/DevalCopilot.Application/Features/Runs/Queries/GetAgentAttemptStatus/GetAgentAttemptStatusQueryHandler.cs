using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;

public sealed class GetAgentAttemptStatusQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetAgentAttemptStatusQuery, Result<AgentAttemptStatusQueryResult>>
{
    // Mirrors the adapter contract version fixed at claim time (Attempt.ClaimAgent) and the
    // shared CodexProcessInvoker's own fixed "--sandbox read-only" / "--ephemeral" arguments —
    // a fixed CLI configuration fact, never a provider-observed result and never invocation
    // eligibility. Duplicated here rather than shared across layers, exactly like
    // ClaudeImplementerAdapterContractVersion is already duplicated for the Implementer role.
    private const string CodexPlannerAdapterContractVersion = "codex-planning-v1";
    private const string ConfiguredCodexCommandSandbox = "read-only";
    private const string ConfiguredCodexRolloutPersistence = "Disabled";

    public async Task<Result<AgentAttemptStatusQueryResult>> HandleAsync(
        GetAgentAttemptStatusQuery query, CancellationToken cancellationToken)
    {
        var runExists = await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken);
        if (!runExists)
        {
            return Result<AgentAttemptStatusQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        Attempt? attempt;
        try
        {
            // Planner-only: this query's own contract (see GetAgentAttemptStatusQuery's doc
            // comment) is "the most recent Codex planning attempt" — without this filter, once a
            // run also has ClaudeCode critical-review attempts, "most recent Agent attempt of any
            // role" would silently start returning the wrong attempt's status under this same
            // endpoint.
            attempt = await dbContext.Attempts
                .AsNoTracking()
                .Where(candidate =>
                    candidate.RunId == query.RunId && candidate.Kind == AttemptKind.Agent && candidate.AgentRole == AgentRole.Planner)
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
            return Result<AgentAttemptStatusQueryResult>.Success(AgentAttemptStatusQueryResult.NoAttempt);
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

        // Lineage only: the source's own number, read scoped to this run. A missing source row
        // leaves the number unknown rather than guessed.
        int? repairSourceAttemptNumber = null;
        if (attempt.AgentRepairSourceAttemptId is { } repairSourceAttemptId)
        {
            repairSourceAttemptNumber = await dbContext.Attempts
                .AsNoTracking()
                .Where(candidate => candidate.Id == repairSourceAttemptId && candidate.RunId == query.RunId)
                .Select(candidate => (int?)candidate.AttemptNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var isCoherentDefaultPlannerAssignment =
            attempt.AgentRole == AgentRole.Planner
            && assignment.Provider == AgentProvider.Codex
            && assignment.PermissionProfile == AgentPermissionProfile.ReadOnly
            && assignment.AdapterContractVersion == CodexPlannerAdapterContractVersion;

        var configuredCommandSandbox = isCoherentDefaultPlannerAssignment ? ConfiguredCodexCommandSandbox : null;
        var configuredRolloutPersistence = isCoherentDefaultPlannerAssignment ? ConfiguredCodexRolloutPersistence : null;

        return Result<AgentAttemptStatusQueryResult>.Success(new AgentAttemptStatusQueryResult(
            true,
            attempt.Id,
            attempt.AttemptNumber,
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
            attempt.AgentRepairSourceAttemptId,
            repairSourceAttemptNumber));
    }

    private static Result<AgentAttemptStatusQueryResult> InvalidAssignment() =>
        Result<AgentAttemptStatusQueryResult>.Failure(
            Error.Failure("agent_attempts.invalid_assignment", "Codex plan assignment metadata is unavailable."));
}
