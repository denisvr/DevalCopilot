using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>
/// The one provider-neutral untracked statement of the ownership-release candidate or receipt (ADR-0065): the run, its (at most two read)
/// sequences, the selected step, its pinned workspace, source checkpoint and recipe, the dispatch witness, the physical termination
/// observation, the release and the header of its event, the linked Agent attempt or verification execution and the queued admission,
/// all read together as left joins keyed by stored identities, so the whole answer comes from one consistent database snapshot. No
/// current, latest or eligibility row is selected and nothing external (Git, filesystem, process, provider, account) is read. The sequences
/// are bounded to two before any join and a final bound caps the result, so damaged storage never yields an unbounded set.
/// </summary>
internal static class LocalExecutionReleaseStatement
{
    /// <summary>The most rows the statement returns: two roots at most. A second row is an ambiguous root, never a partial answer.</summary>
    public const int RowBound = 2;

    public static IQueryable<LocalExecutionReleaseRow> For(IDevalCopilotDbContext dbContext, Guid runId, Guid stepId)
    {
        var sequences = dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(candidate => candidate.RunId == runId)
            .OrderBy(candidate => candidate.Id)
            .Take(2);

        // The release is read by the step it names (unique), so a receipt whose witness or observation disagrees with the step's own is
        // shown to the proof as that disagreement instead of being skipped.
        return (
            from run in dbContext.Runs.AsNoTracking()
            where run.Id == runId
            join sequence in sequences on run.Id equals sequence.RunId into sequenceGroup
            from sequence in sequenceGroup.DefaultIfEmpty()
            join step in dbContext.LocalExecutionSequenceSteps.AsNoTracking().Where(candidate => candidate.Id == stepId)
                on (Guid?)sequence.Id equals (Guid?)step.SequenceId into stepGroup
            from step in stepGroup.DefaultIfEmpty()
            join workspace in dbContext.GitWorkspaces.AsNoTracking()
                on (Guid?)sequence.WorkspaceId equals (Guid?)workspace.Id into workspaceGroup
            from workspace in workspaceGroup.DefaultIfEmpty()
            join source in dbContext.GitCheckpoints.AsNoTracking()
                on (Guid?)step.SourceCheckpointId equals (Guid?)source.Id into sourceGroup
            from source in sourceGroup.DefaultIfEmpty()
            join recipe in dbContext.LocalExecutionSequenceRecipes.AsNoTracking()
                on new { Sequence = (Guid?)sequence.Id, Position = step.RecipeOrdinal }
                equals new { Sequence = (Guid?)recipe.SequenceId, Position = (int?)recipe.Ordinal } into recipeGroup
            from recipe in recipeGroup.DefaultIfEmpty()
            join witness in dbContext.LocalExecutionDispatchWitnesses.AsNoTracking()
                on (Guid?)step.Id equals (Guid?)witness.StepId into witnessGroup
            from witness in witnessGroup.DefaultIfEmpty()
            join observation in dbContext.LocalExecutionTerminationObservations.AsNoTracking()
                on (Guid?)witness.Id equals (Guid?)observation.WitnessId into observationGroup
            from observation in observationGroup.DefaultIfEmpty()
            join release in dbContext.LocalExecutionOwnershipReleases.AsNoTracking()
                on (Guid?)step.Id equals (Guid?)release.StepId into releaseGroup
            from release in releaseGroup.DefaultIfEmpty()
            join releaseByWitness in dbContext.LocalExecutionOwnershipReleases.AsNoTracking()
                on (Guid?)witness.Id equals (Guid?)releaseByWitness.WitnessId into releaseByWitnessGroup
            from releaseByWitness in releaseByWitnessGroup.DefaultIfEmpty()
            join runEvent in dbContext.Events.AsNoTracking()
                on (Guid?)release.EventId equals (Guid?)runEvent.Id into eventGroup
            from runEvent in eventGroup.DefaultIfEmpty()
            join attempt in dbContext.Attempts.AsNoTracking()
                on step.OperationId equals (Guid?)attempt.Id into attemptGroup
            from attempt in attemptGroup.DefaultIfEmpty()
            join execution in dbContext.VerificationExecutions.AsNoTracking()
                on step.OperationId equals (Guid?)execution.Id into executionGroup
            from execution in executionGroup.DefaultIfEmpty()
            join admission in dbContext.LocalExecutionAdmissions.AsNoTracking()
                on (Guid?)step.Id equals admission.StepId into admissionGroup
            from admission in admissionGroup.DefaultIfEmpty()
            select new LocalExecutionReleaseJoin
            {
                Run = run,
                Sequence = sequence,
                Step = step,
                Workspace = workspace,
                Source = source,
                Recipe = recipe,
                Witness = witness,
                Observation = observation,
                Release = release,
                ReleaseByWitness = releaseByWitness,
                Event = runEvent,
                Attempt = attempt,
                Execution = execution,
                Admission = admission,
            })
            .Project(dbContext)
            .Take(RowBound);
    }
}
