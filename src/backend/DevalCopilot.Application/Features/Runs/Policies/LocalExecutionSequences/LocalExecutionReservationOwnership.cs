using System.Linq.Expressions;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The shared, database-side reading of pre-claim reservations as execution owners (ADR-0062). A queued admission that is Reserved or
/// Unresolved owns its exact workspace and, when its step is potentially mutating by Domain's closed step/contract mapping, one global
/// unit, until its claim consumes it, or a proved unused retirement commits. Waiting owns nothing. A reservation counts only while its
/// complete identity is coherent: the exact step of the same sequence, ordinal, kind, pass and source, the matching state, no linked
/// operation, and a queued sequence whose workspace belongs to its project. Anything else is never a zero count: the shared policies
/// treat it as Unavailable.
///
/// <para>
/// A caller that is itself the reservation under judgment (the claim composed from its exact internal ticket) is excluded only after
/// <see cref="ProveOwnAsync"/> proved the full sequence/step/token/operation/admission identity; it is never subtracted by an UUID, run
/// or workspace alone. The reading never reads Git, a provider, a process table or supervision.
/// </para>
/// </summary>
internal static class LocalExecutionReservationOwnership
{
    /// <summary>The result of judging a caller's own ticket: it excludes exactly one reservation step, none (an Immediate sequence has
    /// no reservation), or is invalid, which the policies treat as Unavailable.</summary>
    internal readonly record struct OwnProof(bool IsValid, Guid? ExcludedStepId)
    {
        public static OwnProof None => new(true, null);

        public static OwnProof Invalid => new(false, null);
    }

    /// <summary>Every held admission: Reserved or Unresolved.</summary>
    public static IQueryable<LocalExecutionAdmission> Held(IDevalCopilotDbContext dbContext) =>
        dbContext.LocalExecutionAdmissions.AsNoTracking().Where(admission =>
            admission.State == LocalExecutionAdmissionState.Reserved || admission.State == LocalExecutionAdmissionState.Unresolved);

    /// <summary>The held admission's whole identity is coherent with its step and its queued sequence.</summary>
    public static Expression<Func<LocalExecutionAdmission, bool>> Coherent(IDevalCopilotDbContext dbContext) =>
        admission => dbContext.LocalExecutionSequenceSteps.Any(step => step.Id == admission.StepId
                && step.SequenceId == admission.SequenceId
                && step.Ordinal == admission.Ordinal
                && step.Kind == admission.Kind
                && step.PassNumber == admission.PassNumber
                && step.RecipeOrdinal == admission.RecipeOrdinal
                && step.SourceCheckpointId == admission.SourceCheckpointId
                && step.SourceFingerprintSha256 == admission.SourceFingerprintSha256
                && step.ReportMessageId == admission.ReportMessageId
                && step.SubjectAttemptId == admission.SubjectAttemptId
                && step.PredecessorStepId == admission.PredecessorStepId
                && (admission.Kind == LocalExecutionStepKind.Verification ? admission.RecipeOrdinal != null : admission.RecipeOrdinal == null)
                && step.OperationId == null
                && ((admission.State == LocalExecutionAdmissionState.Reserved && step.State == LocalExecutionSequenceStepState.Reserved)
                    || (admission.State == LocalExecutionAdmissionState.Unresolved && step.State == LocalExecutionSequenceStepState.Unresolved)))
            && dbContext.LocalExecutionSequences.Any(sequence => sequence.Id == admission.SequenceId
                && sequence.AdmissionMode == LocalExecutionAdmissionMode.WaitForAdmission
                && dbContext.Runs.Any(run => run.Id == sequence.RunId && run.ProjectId == sequence.ProjectId)
                && dbContext.GitWorkspaces.Any(workspace => workspace.Id == sequence.WorkspaceId && workspace.ProjectId == sequence.ProjectId)
                && dbContext.GitCheckpoints.Any(checkpoint => checkpoint.Id == sequence.CheckpointId
                    && checkpoint.WorkspaceId == sequence.WorkspaceId
                    && checkpoint.FingerprintSha256 == sequence.FingerprintSha256)
                && dbContext.GitCheckpoints.Any(checkpoint => checkpoint.Id == admission.SourceCheckpointId
                    && checkpoint.WorkspaceId == sequence.WorkspaceId
                    && checkpoint.FingerprintSha256 == admission.SourceFingerprintSha256));

    /// <summary>Exactly the kinds Domain classifies potentially mutating, as one database-side predicate.</summary>
    public static Expression<Func<LocalExecutionAdmission, bool>> Mutating()
    {
        var admission = Expression.Parameter(typeof(LocalExecutionAdmission), "admission");
        Expression body = Expression.Constant(false);
        foreach (var kind in Enum.GetValues<LocalExecutionStepKind>())
        {
            if (!LocalExecutionSequencePolicy.IsPotentiallyMutating(kind))
            {
                continue;
            }

            body = Expression.OrElse(
                body,
                Expression.Equal(Expression.Property(admission, nameof(LocalExecutionAdmission.Kind)), Expression.Constant(kind)));
        }

        return Expression.Lambda<Func<LocalExecutionAdmission, bool>>(body, admission);
    }

    /// <summary>The held admissions of the caller's own ticket removed, so a claim never counts its own reservation.</summary>
    public static IQueryable<LocalExecutionAdmission> Without(IQueryable<LocalExecutionAdmission> held, Guid? excludedStepId) =>
        excludedStepId is { } own ? held.Where(admission => admission.StepId != own) : held;

    /// <summary>
    /// Proves the caller's exact internal ticket before it may exclude its own reservation: an Immediate sequence has no reservation
    /// (nothing to exclude); a queued one must show the exact Reserved admission and step of the ticket's sequence, the original
    /// preparation token and operation identity, kind, ordinal and source. A queued sequence without that proof is invalid.
    /// </summary>
    public static async Task<OwnProof> ProveOwnAsync(
        IDevalCopilotDbContext dbContext, LocalExecutionStepTicket? ticket, CancellationToken cancellationToken)
    {
        if (ticket is null)
        {
            return OwnProof.None;
        }

        var mode = await dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(sequence => sequence.Id == ticket.SequenceId)
            .Select(sequence => (LocalExecutionAdmissionMode?)sequence.AdmissionMode)
            .SingleOrDefaultAsync(cancellationToken);
        if (mode is null)
        {
            return OwnProof.Invalid;
        }

        if (mode == LocalExecutionAdmissionMode.Immediate)
        {
            return OwnProof.None;
        }

        if (!KindMappingHolds(ticket.Kind))
        {
            return OwnProof.Invalid;
        }

        // The whole historical identity (run, project, workspace, checkpoint, fingerprint, complete admission/step tuple) is the shared
        // coherence predicate; the ticket's own carried facts must then equal that same step. No current eligibility is required.
        var proved = await dbContext.LocalExecutionAdmissions.AsNoTracking()
            .Where(admission => admission.StepId == ticket.StepId
                && admission.SequenceId == ticket.SequenceId
                && admission.State == LocalExecutionAdmissionState.Reserved
                && admission.Ordinal == ticket.Ordinal
                && admission.Kind == ticket.Kind
                && admission.SourceCheckpointId == ticket.SourceCheckpointId
                && admission.ReportMessageId == ticket.ReportMessageId
                && admission.SubjectAttemptId == ticket.SubjectAttemptId
                && dbContext.LocalExecutionSequenceSteps.Any(step => step.Id == ticket.StepId
                    && step.SequenceId == ticket.SequenceId
                    && step.State == LocalExecutionSequenceStepState.Reserved
                    && step.PreparationOwnerToken == ticket.OwnerToken
                    && step.ReservedOperationId == ticket.ReservedOperationId
                    && step.Ordinal == ticket.Ordinal
                    && step.Kind == ticket.Kind
                    && step.SourceCheckpointId == ticket.SourceCheckpointId
                    && step.ReportMessageId == ticket.ReportMessageId
                    && step.SubjectAttemptId == ticket.SubjectAttemptId
                    && step.OperationId == null
                    && (ticket.Kind == LocalExecutionStepKind.Verification
                        ? ticket.RecipeId != null
                            && dbContext.LocalExecutionSequenceRecipes.Any(recipe => recipe.SequenceId == ticket.SequenceId
                                && recipe.Ordinal == step.RecipeOrdinal
                                && recipe.VerificationCommandId == ticket.RecipeId)
                        : ticket.RecipeId == null)))
            .Where(Coherent(dbContext))
            .AnyAsync(cancellationToken);
        return proved ? new OwnProof(true, ticket.StepId) : OwnProof.Invalid;
    }

    /// <summary>The closed step-kind mapping: a defined kind is either the Verification step (no Agent contract) or an Agent step whose
    /// contract Domain classifies.</summary>
    public static bool KindMappingHolds(LocalExecutionStepKind kind) =>
        Enum.IsDefined(kind) && (kind == LocalExecutionStepKind.Verification) == (LocalExecutionSequencePolicy.ContractOf(kind) is null);

    /// <summary>
    /// The complete historical identity of one admission and its step, proved from the database exactly as the ownership reading counts it:
    /// run and project, workspace, pinned checkpoint and fingerprint, source checkpoint and fingerprint, the whole admission/step tuple,
    /// the closed kind mapping and the queued mode. It requires no current eligibility and reads no Git, provider or process.
    /// </summary>
    public static async Task<bool> IdentityHoldsAsync(
        IDevalCopilotDbContext dbContext, LocalExecutionAdmission admission, CancellationToken cancellationToken) =>
        KindMappingHolds(admission.Kind)
        && await dbContext.LocalExecutionAdmissions.AsNoTracking()
            .Where(candidate => candidate.Id == admission.Id)
            .Where(Coherent(dbContext))
            .AnyAsync(cancellationToken);

    /// <summary>
    /// Whether a held admission in scope is not coherent (a missing, foreign, duplicate or inconsistent link): such evidence is never a
    /// zero count. <paramref name="projectId"/> scopes the check to one project, or null for every queued sequence.
    /// </summary>
    public static async Task<bool> HasIncoherentReservationAsync(
        IDevalCopilotDbContext dbContext, Guid? projectId, CancellationToken cancellationToken)
    {
        var held = Held(dbContext);
        if (projectId is { } project)
        {
            held = held.Where(admission => dbContext.LocalExecutionSequences.Any(
                sequence => sequence.Id == admission.SequenceId && sequence.ProjectId == project));
        }

        return await held.CountAsync(cancellationToken) != await held.CountAsync(Coherent(dbContext), cancellationToken);
    }
}
