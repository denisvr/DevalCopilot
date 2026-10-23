using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// The one provider-neutral statement of the admission history (ADR-0063): the run, its sequence, the bounded admission ledger and every
/// historical fact its proof needs (each linked step, the pinned workspace and initial checkpoint, each source checkpoint, each report
/// message, each pinned recipe, and bounded counts of the sequence's recipes and steps) read together as left joins, so the whole answer
/// comes from one consistent database snapshot. Joins read the persisted facts by identity only; no latest, current or eligibility row is
/// selected. The sequences are bounded to two and the ledger to <see cref="AdmissionBound"/> before any join, and a final bound caps the
/// result, so damaged storage never yields an unbounded set. Like the other inspection projections, every stored enum is classified by
/// the database, every number is an exact integer in its range, and every text is bounded by its stored bytes before it is converted.
/// </summary>
internal static class LocalExecutionAdmissionHistoryStatement
{
    /// <summary>The most admission rows read: the largest legitimate ledger plus one, so a reachable extra row proves damaged storage.</summary>
    public const int AdmissionBound = LocalExecutionInspectionProjection.StepBound;

    /// <summary>The most rows the statement returns: a legitimate ledger never exceeds the absolute maximum number of steps.</summary>
    public const int RowBound = AdmissionBound + 1;

    public static IQueryable<LocalExecutionAdmissionHistoryRow> For(IDevalCopilotDbContext dbContext, Guid runId)
    {
        var sequences = dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(candidate => candidate.RunId == runId)
            .OrderBy(candidate => candidate.Id)
            .Take(2);
        var admissions = dbContext.LocalExecutionAdmissions.AsNoTracking()
            .Where(candidate => dbContext.LocalExecutionSequences.AsNoTracking()
                .Any(owner => owner.Id == candidate.SequenceId && owner.RunId == runId))
            .OrderBy(candidate => candidate.SequenceId)
            .ThenBy(candidate => candidate.Ordinal)
            .Take(AdmissionBound);

        return (
            from run in dbContext.Runs.AsNoTracking()
            where run.Id == runId
            join sequence in sequences on run.Id equals sequence.RunId into sequenceGroup
            from sequence in sequenceGroup.DefaultIfEmpty()
            join admission in admissions on (Guid?)sequence.Id equals (Guid?)admission.SequenceId into admissionGroup
            from admission in admissionGroup.DefaultIfEmpty()
            join step in dbContext.LocalExecutionSequenceSteps.AsNoTracking() on admission.StepId equals (Guid?)step.Id into stepGroup
            from step in stepGroup.DefaultIfEmpty()
            join workspace in dbContext.GitWorkspaces.AsNoTracking()
                on (Guid?)sequence.WorkspaceId equals (Guid?)workspace.Id into workspaceGroup
            from workspace in workspaceGroup.DefaultIfEmpty()
            join initial in dbContext.GitCheckpoints.AsNoTracking()
                on (Guid?)sequence.CheckpointId equals (Guid?)initial.Id into initialGroup
            from initial in initialGroup.DefaultIfEmpty()
            join source in dbContext.GitCheckpoints.AsNoTracking()
                on (Guid?)admission.SourceCheckpointId equals (Guid?)source.Id into sourceGroup
            from source in sourceGroup.DefaultIfEmpty()
            join report in dbContext.CollaborationMessages.AsNoTracking()
                on admission.ReportMessageId equals (Guid?)report.Id into reportGroup
            from report in reportGroup.DefaultIfEmpty()
            join recipe in dbContext.LocalExecutionSequenceRecipes.AsNoTracking()
                on new { Sequence = (Guid?)sequence.Id, Position = admission.RecipeOrdinal }
                equals new { Sequence = (Guid?)recipe.SequenceId, Position = (int?)recipe.Ordinal } into recipeGroup
            from recipe in recipeGroup.DefaultIfEmpty()
            orderby admission.Ordinal
            select new LocalExecutionAdmissionHistoryJoin
            {
                Run = run,
                Sequence = sequence,
                Admission = admission,
                Step = step,
                Workspace = workspace,
                Initial = initial,
                Source = source,
                Report = report,
                Recipe = recipe,
            })
            .Project(dbContext)
            .Take(RowBound);
    }
}
