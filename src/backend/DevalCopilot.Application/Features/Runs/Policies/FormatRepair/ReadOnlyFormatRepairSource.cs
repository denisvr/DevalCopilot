using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.FormatRepair;

/// <summary>
/// The single source-eligibility rule for the one manual format repair of the three read-only
/// stages beyond the Planner (CriticalReviewer, Resolver, CodeReviewer), evaluated identically at
/// the request and again at the durable claim boundary inside the claim's write-locked transaction.
/// A source is eligible only when it belongs to this run, is a failed, dispatched, concluded attempt
/// of exactly the path's own tuple that ended <see cref="AgentOutcome.InvalidStructuredOutput"/>
/// with clean-exit process evidence (see <see cref="ReadOnlyFormatRepairPolicy"/>), is not itself a
/// repair, produced no durable collaboration message, has not already been repaired, is still the
/// run's latest Agent attempt, and — when a context is supplied — was made against exactly the
/// workspace, checkpoint, and fingerprint the repair claim selected. Every check reads fresh
/// untracked state; failures are fixed, safe results that echo nothing persisted. An unknown source
/// and a source of another run are indistinguishable (one 404). Only <see cref="InvalidOperationException"/>
/// raised while materializing the source row means "unreadable"; database and cancellation failures
/// propagate and are never reported as invalid source evidence.
/// </summary>
internal static class ReadOnlyFormatRepairSource
{
    public const string InputsMismatchCode = "agent_attempts.repair_source_inputs_mismatch";

    /// <summary>The workspace, checkpoint, and fingerprint the repair claim selected.</summary>
    public sealed record Context(Guid WorkspaceId, Guid CheckpointId, string FingerprintSha256);

    public sealed record Evaluation(Attempt? Source, Error? Error)
    {
        public static Evaluation Failed(Error error) => new(null, error);

        public static Evaluation Eligible(Attempt source) => new(source, null);
    }

    public static Error NotFound() => Error.NotFound(
        PlanningRepairSource.NotFoundCode, "The selected attempt was not found for this run.");

    public static Error Ineligible() => Error.Conflict(
        PlanningRepairSource.IneligibleCode, "The selected attempt cannot be repaired.");

    public static Error InputsMismatch() => Error.Conflict(
        InputsMismatchCode,
        "The selected attempt's recorded inputs are no longer the current inputs; use an ordinary request instead.");

    /// <summary>
    /// The repair's execution context, re-read fresh at the durable claim boundary: the run is still active
    /// for this stage, the workspace is still Ready with an active lease, and the selected checkpoint is still
    /// the workspace's latest. The request-time reads happened before the Git capture and manifest seal, so
    /// only this in-transaction read makes "the source's exact context is still current" atomic with the claim.
    /// An unreadable persisted value reads as not current; database and cancellation failures propagate.
    /// </summary>
    public static async Task<Error?> EvaluateContextStillCurrentAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        Guid workspaceId,
        Guid checkpointId,
        bool runMayBeCreated,
        CancellationToken cancellationToken)
    {
        try
        {
            var lifecycle = await dbContext.Runs
                .AsNoTracking()
                .Where(run => run.Id == runId)
                .Select(run => (RunLifecycle?)run.Lifecycle)
                .SingleOrDefaultAsync(cancellationToken);
            if (lifecycle != RunLifecycle.Running && !(runMayBeCreated && lifecycle == RunLifecycle.Created))
            {
                return Error.Conflict(
                    runMayBeCreated ? "runs.not_active" : "runs.not_running", "The run can no longer start this attempt.");
            }

            var workspaceStatus = await dbContext.GitWorkspaces
                .AsNoTracking()
                .Where(workspace => workspace.Id == workspaceId)
                .Select(workspace => (WorkspaceStatus?)workspace.Status)
                .SingleOrDefaultAsync(cancellationToken);
            if (workspaceStatus != WorkspaceStatus.Ready)
            {
                return Error.Conflict("agent_attempts.workspace_not_ready", "The isolated workspace is no longer ready.");
            }

            var leaseIsActive = await dbContext.RepositoryMutationLeases
                .AsNoTracking()
                .AnyAsync(lease => lease.WorkspaceId == workspaceId && lease.Status == LeaseStatus.Active, cancellationToken);
            if (!leaseIsActive)
            {
                return Error.Conflict("agent_attempts.lease_not_active", "The workspace lease is no longer active.");
            }

            var latestCheckpointId = await dbContext.GitCheckpoints
                .AsNoTracking()
                .Where(checkpoint => checkpoint.WorkspaceId == workspaceId)
                .OrderByDescending(checkpoint => checkpoint.CheckpointNumber)
                .Select(checkpoint => (Guid?)checkpoint.Id)
                .FirstOrDefaultAsync(cancellationToken);
            return latestCheckpointId == checkpointId
                ? null
                : Error.Conflict("agent_attempts.checkpoint_not_current", "The selected source checkpoint is no longer current for this workspace.");
        }
        catch (InvalidOperationException)
        {
            return Error.Conflict(PlanningRepairSource.IneligibleCode, "The selected attempt cannot be repaired.");
        }
    }

    public static async Task<Evaluation> EvaluateAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        Guid sourceAttemptId,
        AgentResponseContract responseContract,
        Context? context,
        CancellationToken cancellationToken)
    {
        var sourceExists = await dbContext.Attempts
            .AsNoTracking()
            .AnyAsync(candidate => candidate.Id == sourceAttemptId && candidate.RunId == runId, cancellationToken);
        if (!sourceExists)
        {
            return Evaluation.Failed(NotFound());
        }

        Attempt? source;
        try
        {
            source = await dbContext.Attempts
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == sourceAttemptId && candidate.RunId == runId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // A persisted row whose values cannot be materialized is never a trustworthy source. The
            // exception and the stored value are deliberately not echoed.
            return Evaluation.Failed(Ineligible());
        }

        if (source is null)
        {
            return Evaluation.Failed(NotFound());
        }

        if (source.AgentRepairSourceAttemptId is not null)
        {
            return Evaluation.Failed(Error.Conflict(
                PlanningRepairSource.RepairOfRepairCode, "A repair attempt cannot itself be repaired."));
        }

        if (!ReadOnlyFormatRepairPolicy.IsEligibleSource(source, responseContract) || !AgentAttemptIdentity.IsCoherent(source))
        {
            return Evaluation.Failed(Ineligible());
        }

        // A source that produced any durable collaboration message did not fail structurally; a
        // repair could then create a second semantic result for one review, resolution, or plan.
        var hasSemanticMessage = await dbContext.CollaborationMessages
            .AsNoTracking()
            .AnyAsync(message => message.AttemptId == source.Id, cancellationToken);
        if (hasSemanticMessage)
        {
            return Evaluation.Failed(Ineligible());
        }

        var alreadyRepaired = await dbContext.Attempts
            .AsNoTracking()
            .AnyAsync(candidate => candidate.AgentRepairSourceAttemptId == sourceAttemptId, cancellationToken);
        if (alreadyRepaired)
        {
            return Evaluation.Failed(Error.Conflict(
                PlanningRepairSource.AlreadyRequestedCode, "A repair was already requested for this attempt."));
        }

        var latestAgentAttemptNumber = await dbContext.Attempts
            .AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.Kind == AttemptKind.Agent)
            .MaxAsync(candidate => (int?)candidate.AttemptNumber, cancellationToken);
        if (latestAgentAttemptNumber != source.AttemptNumber)
        {
            return Evaluation.Failed(Error.Conflict(
                PlanningRepairSource.NotLatestCode, "Only the run's latest Agent attempt can be repaired."));
        }

        if (context is not null
            && (source.AgentGitWorkspaceId != context.WorkspaceId
                || source.AgentGitCheckpointId != context.CheckpointId
                || !string.Equals(source.AgentCheckpointFingerprintSha256, context.FingerprintSha256, StringComparison.Ordinal)))
        {
            return Evaluation.Failed(Error.Conflict(
                PlanningRepairSource.CheckpointMismatchCode,
                "The repair must use the same workspace checkpoint as the selected attempt."));
        }

        return Evaluation.Eligible(source);
    }
}
