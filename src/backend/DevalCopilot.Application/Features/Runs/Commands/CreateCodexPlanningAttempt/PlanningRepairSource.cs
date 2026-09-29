using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;

/// <summary>
/// The single source-eligibility rule for the one manual Codex Planner format repair, evaluated
/// identically at the request and again at the durable claim boundary. A source is eligible only
/// when it belongs to this run, is a terminal dispatched Codex Planner/Proposal attempt whose
/// outcome is exactly <see cref="AgentOutcome.InvalidStructuredOutput"/>, is not itself a repair,
/// has not already been repaired, is still the run's latest Agent attempt, and was made against exactly the
/// workspace, checkpoint, and fingerprint the repair claim selected. Every check reads
/// fresh untracked state and never trusts a caller's earlier view; failures are fixed, safe
/// conflicts that disclose nothing from the source.
/// </summary>
internal static class PlanningRepairSource
{
    public const string NotFoundCode = "agent_attempts.repair_source_not_found";
    public const string IneligibleCode = "agent_attempts.repair_source_ineligible";
    public const string RepairOfRepairCode = "agent_attempts.repair_of_repair_forbidden";
    public const string AlreadyRequestedCode = "agent_attempts.repair_already_requested";
    public const string NotLatestCode = "agent_attempts.repair_source_not_latest";
    public const string CheckpointMismatchCode = "agent_attempts.repair_source_checkpoint_mismatch";

    /// <summary>Returns <see langword="null"/> when the source is currently eligible; otherwise the
    /// fixed error. Unknown and foreign-run sources are indistinguishable (one 404). At the claim
    /// boundary this runs after the claim's first write inside its own transaction, so the reads
    /// below cannot interleave with another claim's commit.</summary>
    public static async Task<Error?> EvaluateAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        Guid sourceAttemptId,
        Guid workspaceId,
        Guid checkpointId,
        string checkpointFingerprintSha256,
        CancellationToken cancellationToken)
    {
        Attempt? source;
        try
        {
            source = await dbContext.Attempts
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == sourceAttemptId && candidate.RunId == runId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // A persisted row whose values cannot be materialized is never a trustworthy source.
            return Error.Conflict(IneligibleCode, "The selected attempt cannot be repaired.");
        }

        if (source is null)
        {
            return Error.NotFound(NotFoundCode, "The selected planning attempt was not found for this run.");
        }

        if (source.AgentRepairSourceAttemptId is not null)
        {
            return Error.Conflict(RepairOfRepairCode, "A repair attempt cannot itself be repaired.");
        }

        if (!source.IsEligiblePlanningRepairSource)
        {
            return Error.Conflict(IneligibleCode, "The selected attempt cannot be repaired.");
        }

        var alreadyRepaired = await dbContext.Attempts
            .AsNoTracking()
            .AnyAsync(candidate => candidate.AgentRepairSourceAttemptId == sourceAttemptId, cancellationToken);
        if (alreadyRepaired)
        {
            return Error.Conflict(AlreadyRequestedCode, "A repair was already requested for this attempt.");
        }

        var latestAgentAttemptNumber = await dbContext.Attempts
            .AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.Kind == AttemptKind.Agent)
            .MaxAsync(candidate => (int?)candidate.AttemptNumber, cancellationToken);
        if (latestAgentAttemptNumber != source.AttemptNumber)
        {
            return Error.Conflict(NotLatestCode, "Only the run's latest Agent attempt can be repaired.");
        }

        // The repair must be about exactly the source's own immutable workspace, checkpoint, and
        // fingerprint; a newer valid checkpoint is a different context, so use an ordinary request.
        if (source.AgentGitWorkspaceId != workspaceId
            || source.AgentGitCheckpointId != checkpointId
            || !string.Equals(source.AgentCheckpointFingerprintSha256, checkpointFingerprintSha256, StringComparison.Ordinal))
        {
            return Error.Conflict(
                CheckpointMismatchCode, "The repair must use the same workspace checkpoint as the selected attempt.");
        }

        return null;
    }
}
