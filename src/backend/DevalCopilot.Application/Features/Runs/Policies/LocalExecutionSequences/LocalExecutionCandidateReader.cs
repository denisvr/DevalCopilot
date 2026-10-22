using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.Abandonment;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// Reads, from fresh untracked rows only, the one candidate a Human may confirm to arm a local execution sequence (ADR-0060), or the
/// first fixed refusal in a fixed order. It performs no Git, provider or process observation and no execution: Git freshness is the
/// arming command's own capture outside its transaction, and capacity is decided again by the first claim. Reading is bounded: at most
/// 33 enabled recipes are read, and more than 32 refuse instead of returning a prefix.
/// </summary>
internal static class LocalExecutionCandidateReader
{
    internal sealed record Reading(Error? Error, LocalExecutionCandidate? Candidate, Run? Run, GitWorkspace? Workspace, GitCheckpoint? Checkpoint);

    public static async Task<Reading> ReadAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        Guid planProposalMessageId,
        CancellationToken cancellationToken,
        LocalExecutionAdmissionMode mode = LocalExecutionAdmissionMode.Immediate)
    {
        try
        {
            return await ReadCoreAsync(dbContext, runId, planProposalMessageId, mode, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or JsonException)
        {
            // A stored value of a row this reading needs could not be materialized: never a guessed candidate.
            return Refused(LocalExecutionSequenceErrors.SettingsUnavailable());
        }
    }

    private static async Task<Reading> ReadCoreAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        Guid planProposalMessageId,
        LocalExecutionAdmissionMode mode,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == runId, cancellationToken);
        if (run is null)
        {
            return Refused(LocalExecutionSequenceErrors.NotFound());
        }

        if (await CurrentRunExecutionMode.ReadAsync(dbContext, run.Id, cancellationToken) != RunExecutionMode.ManualAgent)
        {
            return Refused(LocalExecutionSequenceErrors.RunNotManual());
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Refused(LocalExecutionSequenceErrors.RunNotRunning());
        }

        if (await dbContext.Attempts.AsNoTracking().AnyAsync(
                attempt => attempt.RunId == run.Id
                    && (attempt.Status == AttemptStatus.Running
                        || attempt.AgentResponseContract == AgentResponseContract.ImplementationReport
                        || attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection),
                cancellationToken)
            || await dbContext.LocalCommitOperations.AsNoTracking().AnyAsync(operation => operation.RunId == run.Id, cancellationToken))
        {
            return Refused(LocalExecutionSequenceErrors.PriorMutation());
        }

        if (await PlanningSequenceFence.IsRunBlockedAsync(dbContext, run.Id, cancellationToken))
        {
            return Refused(LocalExecutionSequenceErrors.PlanningActive());
        }

        // ADR-0062: a queued intake may wait behind a coherent workspace owner, but a project blocker is tolerated only after its cause
        // is proved to be exactly that owner (below); an Immediate arming still refuses it here.
        var projectBlocker = await RunAbandonmentAuthority.FindBlockerAsync(dbContext, run.ProjectId, cancellationToken);
        if (projectBlocker is not null && mode == LocalExecutionAdmissionMode.Immediate)
        {
            return Refused(LocalExecutionSequenceErrors.ProjectBusy());
        }

        var workspace = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Refused(LocalExecutionSequenceErrors.WorkspaceNotReady());
        }

        if (!await dbContext.RepositoryMutationLeases.AsNoTracking().AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return Refused(LocalExecutionSequenceErrors.LeaseNotActive());
        }

        var checkpoint = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Refused(LocalExecutionSequenceErrors.CheckpointMissing());
        }

        if (mode == LocalExecutionAdmissionMode.Immediate)
        {
            if (await WorkspaceExecutionFence.CheckClaimAsync(dbContext, run.ProjectId, workspace.Id, cancellationToken) is not null)
            {
                return Refused(LocalExecutionSequenceErrors.WorkspaceOwned());
            }

            if (await ExecutionCapacityFence.CheckClaimAsync(dbContext, cancellationToken) is not null)
            {
                return Refused(LocalExecutionSequenceErrors.CapacityUnavailable());
            }
        }
        else
        {
            var queueRefusal = await CheckQueueAdmissionAsync(dbContext, run.ProjectId, workspace.Id, projectBlocker is not null, cancellationToken);
            if (queueRefusal is not null)
            {
                return Refused(queueRefusal);
            }
        }

        var (plan, planError) = await LocalExecutionPlanEligibility.EvaluateAsync(
            dbContext, run, planProposalMessageId, workspace, checkpoint, cancellationToken);
        if (plan is null)
        {
            return Refused(planError ?? LocalExecutionSequenceErrors.PlanNotEligible());
        }

        var recipes = await ReadRecipesAsync(dbContext, run.ProjectId, cancellationToken);
        if (recipes.Error is not null)
        {
            return Refused(recipes.Error);
        }

        var settings = LocalExecutionSettingsSnapshot.Of(run);
        if (settings is null)
        {
            return Refused(LocalExecutionSequenceErrors.SettingsUnavailable());
        }

        var corrections = await dbContext.Attempts.AsNoTracking().CountAsync(
            attempt => attempt.RunId == run.Id && attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection, cancellationToken);
        var remaining = Math.Max(run.MaximumReviewCorrectionAttempts - corrections, 0);
        var allowance = Math.Min(remaining, LocalExecutionSequencePolicy.MaximumCorrections);

        var candidate = new LocalExecutionCandidate(
            run.Id,
            run.ProjectId,
            planProposalMessageId,
            plan.Authority,
            plan.OrderedInputMessageIds,
            plan.GrantId,
            workspace.Id,
            checkpoint.Id,
            checkpoint.FingerprintSha256,
            settings,
            allowance,
            recipes.Pins!);
        return new Reading(null, candidate, run, workspace, checkpoint);
    }

    /// <summary>The project's whole enabled recipe set, ordered by CommandNumber then Id, pinned by value. Unique identities and numbers
    /// are required; 1 to 32 recipes; anything else refuses, never truncates.</summary>
    public static async Task<(Error? Error, IReadOnlyList<LocalExecutionRecipePin>? Pins)> ReadRecipesAsync(
        IDevalCopilotDbContext dbContext, Guid projectId, CancellationToken cancellationToken)
    {
        var commands = await dbContext.VerificationCommands.AsNoTracking()
            .Where(command => command.ProjectId == projectId && command.IsEnabled)
            .OrderBy(command => command.CommandNumber)
            .ThenBy(command => command.Id)
            .Take(LocalExecutionSequencePolicy.MaximumRecipes + 1)
            .ToListAsync(cancellationToken);
        if (commands.Count == 0)
        {
            return (LocalExecutionSequenceErrors.RecipesMissing(), null);
        }

        if (commands.Count > LocalExecutionSequencePolicy.MaximumRecipes)
        {
            return (LocalExecutionSequenceErrors.RecipesTooMany(), null);
        }

        if (commands.Select(command => command.Id).Distinct().Count() != commands.Count
            || commands.Select(command => command.CommandNumber).Distinct().Count() != commands.Count
            || commands.Any(command => command.TimeoutSeconds is < 1 or > VerificationCommand.MaximumTimeoutSeconds
                || string.IsNullOrWhiteSpace(command.Name)
                || string.IsNullOrWhiteSpace(command.ExecutablePath)))
        {
            return (LocalExecutionSequenceErrors.RecipesIncoherent(), null);
        }

        var pins = commands
            .Select((command, index) => new LocalExecutionRecipePin(
                index,
                command.Id,
                command.CommandNumber,
                command.Name,
                command.ExecutablePath,
                JsonSerializer.Serialize(command.Arguments),
                command.TimeoutSeconds,
                command.IsEnabled,
                command.ConfiguredAtUtc,
                command.UpdatedAtUtc))
            .ToList();
        return (null, pins);
    }

    /// <summary>
    /// ADR-0062 queue intake. A coherent busy workspace owner (an Agent attempt, a verification execution, a local-commit reservation
    /// or a queued preparation) and a global AtCapacity reading may be waited for; Conflicted, Unavailable or missing ownership, an
    /// untrustworthy capacity reading and any project blocker whose cause is not that very owner refuse. The bound of live queued
    /// cycles reads at most one more than the limit and never materializes a historical set.
    /// </summary>
    private static async Task<Error?> CheckQueueAdmissionAsync(
        IDevalCopilotDbContext dbContext, Guid projectId, Guid workspaceId, bool projectBlocked, CancellationToken cancellationToken)
    {
        var assessment = await WorkspaceExecutionPolicy.AssessAsync(dbContext, projectId, workspaceId, null, null, cancellationToken);
        var idle = assessment.Status == WorkspaceExecutionStatus.Idle;
        var coherentBusy = assessment.Status is WorkspaceExecutionStatus.AgentAttemptActive
            or WorkspaceExecutionStatus.VerificationActive or WorkspaceExecutionStatus.CommitReserved
            or WorkspaceExecutionStatus.PreparationReserved;
        if (!idle && !coherentBusy)
        {
            return LocalExecutionSequenceErrors.WorkspaceOwned();
        }

        if (projectBlocked && idle)
        {
            return LocalExecutionSequenceErrors.ProjectBusy();
        }

        var capacity = await ExecutionCapacityPolicy.ReadAsync(dbContext, null, cancellationToken);
        if (capacity.Status == ExecutionCapacityStatus.Unavailable)
        {
            return LocalExecutionSequenceErrors.CapacityUnavailable();
        }

        var live = await dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(sequence => sequence.AdmissionMode == LocalExecutionAdmissionMode.WaitForAdmission
                && (sequence.State == LocalExecutionSequenceState.WaitingForAdmission
                    || sequence.State == LocalExecutionSequenceState.PreparingStep
                    || sequence.State == LocalExecutionSequenceState.AwaitingStepResult
                    || sequence.State == LocalExecutionSequenceState.Stopping))
            .Take(LocalExecutionSequencePolicy.MaximumQueuedCycles + 1)
            .CountAsync(cancellationToken);
        return live >= LocalExecutionSequencePolicy.MaximumQueuedCycles ? LocalExecutionSequenceErrors.QueueFull() : null;
    }

    private static Reading Refused(Error error) => new(error, null, null, null, null);
}
