using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;

/// <summary>
/// The one provider-neutral statement of the outstanding queue admissions (ADR-0064). Membership starts from the admissions, never from a
/// run, project or latest row: every admission that is not a recognized Consumed or Retired one, in recorded admission order, at most
/// <see cref="CandidateBound"/> of them so a reachable extra candidate proves overflow. Each candidate's sequence (and any other sequence
/// of the same run, so a duplicate is seen), registered project label and bounded ledger with every historical fact the ADR-0063 proof
/// needs are read as left joins in the same statement, so the answer is one database snapshot. The ledger of a sequence is bounded to
/// <see cref="LocalExecutionAdmissionHistoryStatement.AdmissionBound"/> rows and the whole result to <see cref="RowCeiling"/>, so damaged
/// storage never yields an unbounded set. Nothing here reads a latest, current or eligibility row.
/// </summary>
internal static class LocalExecutionQueueInspectionStatement
{
    /// <summary>The most candidates read: the largest coherent set (64 live queued cycles plus four held ended reservations) plus one.</summary>
    public const int CandidateBound =
        LocalExecutionSequencePolicy.MaximumQueuedCycles + LocalExecutionSequencePolicy.MaximumHeldPreparations + 1;

    /// <summary>The most rows returned: every candidate's ledger bound plus one, so join expansion proves itself.</summary>
    public const int RowCeiling = CandidateBound * LocalExecutionAdmissionHistoryStatement.RowBound + 1;

    public static IQueryable<LocalExecutionQueueInspectionRow> For(IDevalCopilotDbContext dbContext)
    {
        var candidates = dbContext.LocalExecutionAdmissions.AsNoTracking()
            .Where(LocalExecutionQueueInspectionProjection.IsCandidate)
            .OrderBy(candidate => candidate.AdmissionNumber)
            .ThenBy(candidate => candidate.Id)
            .Take(CandidateBound);
        var candidateSequenceIds = candidates.Select(candidate => candidate.SequenceId);
        var runSequences = dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(sequence => dbContext.LocalExecutionSequences.AsNoTracking()
                .Any(owner => candidateSequenceIds.Contains(owner.Id) && owner.RunId == sequence.RunId));
        var ledgers = dbContext.LocalExecutionAdmissions.AsNoTracking()
            .Where(admission => candidateSequenceIds.Contains(admission.SequenceId));

        return (
            from candidate in candidates
            join owner in dbContext.LocalExecutionSequences.AsNoTracking()
                on candidate.SequenceId equals owner.Id into ownerGroup
            from owner in ownerGroup.DefaultIfEmpty()
            from sequence in runSequences
                .Where(member => member.RunId == owner.RunId)
                .OrderBy(member => member.Id)
                .Take(2)
                .DefaultIfEmpty()
            from admission in ledgers
                .Where(member => member.SequenceId == sequence.Id)
                .OrderBy(member => member.Ordinal)
                .Take(LocalExecutionAdmissionHistoryStatement.AdmissionBound)
                .DefaultIfEmpty()
            join run in dbContext.Runs.AsNoTracking() on (Guid?)sequence.RunId equals (Guid?)run.Id into runGroup
            from run in runGroup.DefaultIfEmpty()
            join project in dbContext.Projects.AsNoTracking() on (Guid?)run.ProjectId equals (Guid?)project.Id into projectGroup
            from project in projectGroup.DefaultIfEmpty()
            join step in dbContext.LocalExecutionSequenceSteps.AsNoTracking()
                on admission.StepId equals (Guid?)step.Id into stepGroup
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
            orderby candidate.AdmissionNumber, candidate.Id, sequence.Id, admission.Ordinal
            select new LocalExecutionQueueInspectionJoin
            {
                Candidate = candidate,
                Project = project,
                History = new LocalExecutionAdmissionHistoryJoin
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
                },
            })
            .Select(LocalExecutionQueueInspectionProjection.RowOf(dbContext))
            .Take(RowCeiling);
    }
}
