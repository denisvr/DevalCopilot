using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// Proves the whole recorded structure of one run's local execution sequence from bounded scalar reads (ADR-0061), for the index, every
/// selected step and every sealed window, which deliberately change together. It reads at most 103 step rows and 33 pinned recipe rows and
/// enforces ADR-0060's actual recipe-dependent step bound, unique gapless recipe positions and step ordinals, valid kind, pass and recipe
/// shapes, one predecessor chain, fixed reason codes and the reserved/claimed link shape, together with the historical ownership and
/// source identity: the ManualAgent run, the sequence's own run and project, the pinned workspace and initial checkpoint, each step's
/// source checkpoint in that workspace with its fingerprint, the plan as a Proposal and every report as an ExecutionReport of this run, and
/// every correction's earlier linked subject of the matching origin. Any malformed set is Unavailable as a whole, never a partial list.
/// The sequence revision is read before and after the projection, and a change withholds the whole answer with no retry. No current
/// recipe, message, checkpoint, workspace, lease, eligibility, Git, filesystem, process, provider or account reader is consulted. It
/// writes nothing and swallows no fault.
/// </summary>
internal static class LocalExecutionInspectionReader
{
    private const int MaximumRecipeNameLength = 200;

    public static async Task<LocalExecutionInspectionReading> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking()
            .Where(candidate => candidate.Id == runId)
            .ProjectRun()
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return LocalExecutionInspectionReading.RunNotFound;
        }

        // At most one by the unique run index; observing a second is damaged storage, never a choice between two.
        var sequences = await dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(candidate => candidate.RunId == runId)
            .Take(2)
            .ProjectSequence()
            .ToArrayAsync(cancellationToken);
        if (sequences.Length == 0)
        {
            return LocalExecutionInspectionReading.NotRecorded;
        }

        var sequence = sequences[0];
        if (sequences.Length > 1)
        {
            return LocalExecutionInspectionReading.Unavailable(sequence.Id);
        }

        var structure = await ProveAsync(dbContext, run, sequence, cancellationToken);
        return structure is null
            ? LocalExecutionInspectionReading.Unavailable(sequence.Id)
            : LocalExecutionInspectionReading.Recorded(structure);
    }

    /// <summary>The stored kind of the step by its exact identity within the sequence, as the database's own classification (0 unknown),
    /// or null when the sequence has no such step. It proves nothing about the step beyond its identity and kind.</summary>
    public static async Task<LocalExecutionStepKind?> FindStepKindAsync(
        IDevalCopilotDbContext dbContext, Guid sequenceId, Guid stepId, CancellationToken cancellationToken)
    {
        var kinds = await dbContext.LocalExecutionSequenceSteps.AsNoTracking()
            .Where(step => step.SequenceId == sequenceId && step.Id == stepId)
            .Take(2)
            .Select(step => step.Kind == LocalExecutionStepKind.Implementation ? 1
                : step.Kind == LocalExecutionStepKind.Verification ? 2
                : step.Kind == LocalExecutionStepKind.CodeReview ? 3
                : step.Kind == LocalExecutionStepKind.VerificationDiagnosis ? 4
                : step.Kind == LocalExecutionStepKind.ReviewCorrection ? 5
                : step.Kind == LocalExecutionStepKind.DiagnosisCorrection ? 6
                : 0)
            .ToArrayAsync(cancellationToken);
        return kinds.Length == 1 && kinds[0] is >= 1 and <= 6 ? (LocalExecutionStepKind)(kinds[0] - 1) : null;
    }

    /// <summary>Whether the sequence has exactly one step with this identity, whatever its stored kind.</summary>
    public static async Task<bool> HasStepAsync(
        IDevalCopilotDbContext dbContext, Guid sequenceId, Guid stepId, CancellationToken cancellationToken) =>
        await dbContext.LocalExecutionSequenceSteps.AsNoTracking()
            .AnyAsync(step => step.SequenceId == sequenceId && step.Id == stepId, cancellationToken);

    private static async Task<LocalExecutionInspectionStructure?> ProveAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionInspectionRunRow run,
        LocalExecutionInspectionSequenceRow sequence,
        CancellationToken cancellationToken)
    {
        if (RunExecutionModeStorage.Read(run.StoredMode) != RunExecutionMode.ManualAgent
            || run.Id == Guid.Empty
            || sequence.RunId != run.Id
            || sequence.ProjectId != run.ProjectId
            || sequence.Id == Guid.Empty
            || sequence.WorkspaceId == Guid.Empty
            || sequence.CheckpointId == Guid.Empty
            || sequence.PlanProposalMessageId == Guid.Empty
            || !LocalExecutionSequencePolicy.IsLowercaseSha256(sequence.Fingerprint)
            || sequence.PlanAuthorityCode is < 1 or > 3
            || sequence.StateCode is < 1 or > 10
            || sequence.AdmissionModeCode is < 1 or > 2
            || (sequence.StateCode == 10 && sequence.AdmissionModeCode != 2)
            || sequence.RecipeCount is not { } recipeCount
            || sequence.CorrectionAllowance is not { } correctionAllowance
            || sequence.Revision is not { } revision
            || sequence.ReasonOversized
            || !IsUtc(sequence.ArmedAtUtc)
            || !IsUtc(sequence.UpdatedAtUtc)
            || sequence.UpdatedAtUtc < sequence.ArmedAtUtc)
        {
            return null;
        }

        var state = (LocalExecutionSequenceState)(sequence.StateCode - 1);
        var ended = LocalExecutionSequencePolicy.IsEnded(state);
        if (ended != (sequence.EndedAtUtc is not null)
            || (ended ? !LocalExecutionSequenceReason.IsKnown(sequence.ReasonCode) : sequence.ReasonCode is not null)
            || (sequence.EndedAtUtc is { } endedAtUtc && (!IsUtc(endedAtUtc) || endedAtUtc < sequence.ArmedAtUtc)))
        {
            return null;
        }

        var recipes = await dbContext.LocalExecutionSequenceRecipes.AsNoTracking()
            .Where(recipe => recipe.SequenceId == sequence.Id)
            .OrderBy(recipe => recipe.Ordinal)
            .Take(LocalExecutionInspectionProjection.RecipeBound)
            .ProjectRecipe()
            .ToArrayAsync(cancellationToken);
        var recipeNames = ProveRecipes(recipes, recipeCount);
        if (recipeNames is null)
        {
            return null;
        }

        var rows = await dbContext.LocalExecutionSequenceSteps.AsNoTracking()
            .Where(step => step.SequenceId == sequence.Id)
            .OrderBy(step => step.Ordinal)
            .Take(LocalExecutionInspectionProjection.StepBound)
            .ProjectStep()
            .ToArrayAsync(cancellationToken);
        var steps = ProveSteps(rows, sequence, recipeCount, recipeNames);
        if (steps is null || !await ProveReferencesAsync(dbContext, run, sequence, steps, cancellationToken))
        {
            return null;
        }

        // The revision read before the projection must still be the stored one: a concurrent transition during the bounded structural
        // reads withholds the whole answer. An explicit reload may read the later revision; nothing is retried here.
        var after = await dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(candidate => candidate.Id == sequence.Id)
            .Take(2)
            .ProjectRevision()
            .ToArrayAsync(cancellationToken);
        if (after.Length != 1 || after[0] != revision)
        {
            return null;
        }

        var header = new LocalExecutionInspectionHeader(
            sequence.Id,
            sequence.PlanProposalMessageId,
            (LocalExecutionPlanAuthority)(sequence.PlanAuthorityCode - 1),
            sequence.WorkspaceId,
            sequence.CheckpointId,
            sequence.Fingerprint!,
            state,
            sequence.ReasonCode,
            revision,
            recipeCount,
            correctionAllowance,
            sequence.ArmedAtUtc,
            sequence.UpdatedAtUtc,
            sequence.EndedAtUtc,
            (LocalExecutionAdmissionMode)(sequence.AdmissionModeCode - 1));
        return new LocalExecutionInspectionStructure(run.Id, run.ProjectId, header, steps);
    }

    /// <summary>The recipe names by position when the pinned set is exactly the recorded count with unique gapless positions, strictly
    /// ascending positive command numbers, unique recipe identities, the enabled flag and a bounded nonblank name; otherwise null.</summary>
    private static string[]? ProveRecipes(LocalExecutionInspectionRecipeRow[] recipes, int recipeCount)
    {
        if (recipes.Length != recipeCount)
        {
            return null;
        }

        var names = new string[recipeCount];
        var commandIds = new HashSet<Guid>();
        var previousNumber = 0;
        for (var index = 0; index < recipes.Length; index++)
        {
            var recipe = recipes[index];
            if (recipe.Ordinal != index
                || recipe.CommandNumber is not { } number
                || number <= previousNumber
                || recipe.VerificationCommandId == Guid.Empty
                || !commandIds.Add(recipe.VerificationCommandId)
                || recipe.EnabledStored is not [(byte)'1']
                || recipe.NameOversized
                || string.IsNullOrWhiteSpace(recipe.Name)
                || recipe.Name.Length > MaximumRecipeNameLength)
            {
                return null;
            }

            previousNumber = number;
            names[index] = recipe.Name;
        }

        return names;
    }

    private static LocalExecutionInspectionStep[]? ProveSteps(
        LocalExecutionInspectionStepRow[] rows,
        LocalExecutionInspectionSequenceRow sequence,
        int recipeCount,
        string[] recipeNames)
    {
        if (rows.Length > LocalExecutionSequencePolicy.MaximumSteps(recipeCount))
        {
            return null;
        }

        var steps = new LocalExecutionInspectionStep[rows.Length];
        var stepIds = new HashSet<Guid>();
        var reservedOperationIds = new HashSet<Guid>();
        var operationIds = new HashSet<Guid>();
        var memberships = new HashSet<(int Pass, int Recipe)>();
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            if (row.Ordinal != index
                || row.KindCode is < 1 or > 6
                || row.StateCode is < 1 or > 5
                || row.PassNumber is not { } pass
                || row.Id == Guid.Empty
                || !stepIds.Add(row.Id)
                || row.ReservedOperationId == Guid.Empty
                || !reservedOperationIds.Add(row.ReservedOperationId)
                || row.SourceCheckpointId == Guid.Empty
                || !LocalExecutionSequencePolicy.IsLowercaseSha256(row.SourceFingerprint)
                || row.ReasonOversized)
            {
                return null;
            }

            var kind = (LocalExecutionStepKind)(row.KindCode - 1);
            var state = (LocalExecutionSequenceStepState)(row.StateCode - 1);
            var isImplementation = kind == LocalExecutionStepKind.Implementation;
            var isVerification = kind == LocalExecutionStepKind.Verification;
            var isCorrection = LocalExecutionSequencePolicy.IsCorrectionKind(kind);
            if (isImplementation != (pass == 0)
                || isImplementation != (index == 0)
                || isVerification != row.RecipePresent
                || (isVerification && (row.RecipeOrdinal is not { } position || position >= recipeCount))
                || isImplementation != (row.PredecessorStepId is null)
                || (!isImplementation && row.PredecessorStepId != steps[index - 1].StepId)
                || isImplementation != (row.ReportMessageId is null)
                || row.ReportMessageId == Guid.Empty
                || isCorrection != (row.SubjectAttemptId is not null)
                || row.SubjectAttemptId == Guid.Empty)
            {
                return null;
            }

            if (isVerification && !memberships.Add((pass, row.RecipeOrdinal!.Value)))
            {
                return null;
            }

            // Only a Claimed or Concluded step is linked: its link is exactly the identity chosen at reservation, with its claim time and
            // result deadline. A Reserved, Revoked or Unresolved step has none; only a Revoked or Unresolved one carries a fixed reason.
            var linked = state is LocalExecutionSequenceStepState.Claimed or LocalExecutionSequenceStepState.Concluded;
            var reasoned = state is LocalExecutionSequenceStepState.Revoked or LocalExecutionSequenceStepState.Unresolved;
            if ((row.OperationId is not null) != linked
                || (linked && (row.OperationId != row.ReservedOperationId || !operationIds.Add(row.OperationId.Value)))
                || (row.ClaimedAtUtc is not null) != linked
                || (row.ResultDeadlineUtc is not null) != linked
                || (reasoned ? !LocalExecutionSequenceReason.IsKnown(row.ReasonCode) : row.ReasonCode is not null)
                || (index < rows.Length - 1 && state != LocalExecutionSequenceStepState.Concluded)
                || !IsUtc(row.PreparationReservedAtUtc)
                || !IsUtc(row.PreparationDeadlineUtc)
                || row.PreparationDeadlineUtc <= row.PreparationReservedAtUtc
                || (linked && (!IsUtc(row.ClaimedAtUtc!.Value)
                    || !IsUtc(row.ResultDeadlineUtc!.Value)
                    || row.ClaimedAtUtc < row.PreparationReservedAtUtc
                    || row.ResultDeadlineUtc <= row.ClaimedAtUtc)))
            {
                return null;
            }

            // The implementation operates on the starting checkpoint pinned at arming.
            if (isImplementation
                && (row.SourceCheckpointId != sequence.CheckpointId
                    || !string.Equals(row.SourceFingerprint, sequence.Fingerprint, StringComparison.Ordinal)))
            {
                return null;
            }

            // A correction names its earlier linked review (or diagnosis) attempt of the matching origin, never an arbitrary attempt.
            if (isCorrection)
            {
                var originKind = kind == LocalExecutionStepKind.ReviewCorrection
                    ? LocalExecutionStepKind.CodeReview
                    : LocalExecutionStepKind.VerificationDiagnosis;
                if (!steps.Take(index).Any(earlier => earlier.Kind == originKind && earlier.OperationId == row.SubjectAttemptId))
                {
                    return null;
                }
            }

            steps[index] = new LocalExecutionInspectionStep(
                row.Id,
                index,
                kind,
                pass,
                row.RecipeOrdinal,
                row.RecipeOrdinal is { } recipe ? recipeNames[recipe] : null,
                state,
                row.ReasonCode,
                row.SourceCheckpointId,
                row.SourceFingerprint!,
                row.ReportMessageId,
                row.SubjectAttemptId,
                row.OperationId,
                row.PreparationReservedAtUtc,
                row.PreparationDeadlineUtc,
                row.ClaimedAtUtc,
                row.ResultDeadlineUtc);
        }

        return steps;
    }

    /// <summary>The recorded references by identity: the pinned workspace of the run's project, the initial and every step source
    /// checkpoint in that workspace with the recorded fingerprint, the plan as a Proposal and every report as an ExecutionReport of this
    /// run. A missing, foreign, wrongly typed or oversized reference is incoherent; nothing is repaired from a latest row.</summary>
    private static async Task<bool> ProveReferencesAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionInspectionRunRow run,
        LocalExecutionInspectionSequenceRow sequence,
        LocalExecutionInspectionStep[] steps,
        CancellationToken cancellationToken)
    {
        var workspaceProjects = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(workspace => workspace.Id == sequence.WorkspaceId)
            .Take(2)
            .Select(workspace => workspace.ProjectId)
            .ToArrayAsync(cancellationToken);
        if (workspaceProjects.Length != 1 || workspaceProjects[0] != run.ProjectId)
        {
            return false;
        }

        var expectedFingerprints = new Dictionary<Guid, string> { [sequence.CheckpointId] = sequence.Fingerprint! };
        foreach (var step in steps)
        {
            if (expectedFingerprints.TryGetValue(step.SourceCheckpointId, out var known))
            {
                if (!string.Equals(known, step.SourceFingerprintSha256, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            else
            {
                expectedFingerprints[step.SourceCheckpointId] = step.SourceFingerprintSha256;
            }
        }

        var checkpointIds = expectedFingerprints.Keys.ToArray();
        var checkpoints = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(checkpoint => checkpointIds.Contains(checkpoint.Id))
            .Take(checkpointIds.Length + 1)
            .ProjectCheckpoint()
            .ToArrayAsync(cancellationToken);
        if (checkpoints.Length != checkpointIds.Length
            || checkpoints.Any(checkpoint => checkpoint.FingerprintOversized
                || checkpoint.WorkspaceId != sequence.WorkspaceId
                || !expectedFingerprints.TryGetValue(checkpoint.Id, out var expected)
                || !string.Equals(checkpoint.Fingerprint, expected, StringComparison.Ordinal)))
        {
            return false;
        }

        var reportIds = steps.Where(step => step.ReportMessageId is not null).Select(step => step.ReportMessageId!.Value).Distinct().ToArray();
        var messageIds = reportIds.Append(sequence.PlanProposalMessageId).Distinct().ToArray();
        var messages = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => messageIds.Contains(message.Id))
            .Take(messageIds.Length + 1)
            .ProjectMessage()
            .ToArrayAsync(cancellationToken);
        return messages.Length == messageIds.Length
            && messages.All(message => message.RunId == run.Id)
            && messages.Any(message => message.Id == sequence.PlanProposalMessageId && message.IsProposal)
            && reportIds.All(report => messages.Any(message => message.Id == report && message.IsExecutionReport));
    }

    private static bool IsUtc(DateTimeOffset value) => value != default && value.Offset == TimeSpan.Zero;
}
