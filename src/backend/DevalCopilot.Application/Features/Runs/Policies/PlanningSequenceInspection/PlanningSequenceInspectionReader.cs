using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>
/// Proves the whole recorded structure of one run's planning sequence from bounded scalar reads (ADR-0068), for the index and every
/// selected step, which deliberately change together. It reads at most six step rows, so a sixth is detected, and enforces ADR-0055's fixed
/// table: unique nonempty identities, gapless ordinals 0 to 4, the Planner, CriticalReviewer, Resolver, CriticalReviewer, Resolver roles,
/// known sequence and step states and fixed reason codes, positive safe revisions, canonical UTC times in order, the claimed versus
/// unlinked shape (a Claimed or Concluded step is linked to exactly its reserved attempt identity, every other state to none), every
/// earlier step Concluded, the one predecessor chain and the input Proposals as Proposals of this run (a reviewer's attributed to its
/// predecessor attempt, a Resolver's the same as its preceding review). The ManualAgent run, the sequence's own run and project, the
/// pinned workspace of that project and the pinned checkpoint's workspace and fingerprint are proved by identity. Any malformed set is
/// Unavailable as a whole, never a partial list. The sequence revision is read before and after the structure, and a change withholds the
/// whole answer with no retry. No message content, whole-run lineage, ordered attempt input or output, current lease, latest workspace or
/// checkpoint, Git, filesystem, process, provider or account reader is consulted, and the recorded state is bookkeeping, not acceptance.
/// It writes nothing and swallows no fault.
/// </summary>
internal static class PlanningSequenceInspectionReader
{
    public static async Task<PlanningSequenceInspectionReading> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking()
            .Where(candidate => candidate.Id == runId)
            .ProjectRun()
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return PlanningSequenceInspectionReading.RunNotFound;
        }

        // At most one by the unique run index; observing a second is damaged storage, never a choice between two.
        var sequences = await dbContext.PlanningSequences.AsNoTracking()
            .Where(candidate => candidate.RunId == runId)
            .Take(2)
            .ProjectSequence()
            .ToArrayAsync(cancellationToken);
        if (sequences.Length == 0)
        {
            return PlanningSequenceInspectionReading.NotRecorded;
        }

        var sequence = sequences[0];
        var sequenceId = PlanningSequenceInspectionStorage.ParseIdentifier(sequence.Id);
        if (sequences.Length > 1 || sequenceId is not { } id)
        {
            return PlanningSequenceInspectionReading.Unavailable(sequenceId);
        }

        var structure = await ProveAsync(dbContext, runId, run, sequence, id, cancellationToken);
        return structure is null
            ? PlanningSequenceInspectionReading.Unavailable(id)
            : PlanningSequenceInspectionReading.Recorded(structure);
    }

    /// <summary>Whether this run's recorded sequence has exactly one step with this identity, decided by the database's own join of the
    /// stored identities (never by a parsed value), so a damaged row elsewhere cannot make a foreign or missing step look like a member.</summary>
    public static async Task<bool> HasStepAsync(
        IDevalCopilotDbContext dbContext, Guid runId, Guid stepId, CancellationToken cancellationToken) =>
        await dbContext.PlanningSequences.AsNoTracking()
            .Where(sequence => sequence.RunId == runId)
            .Join(
                dbContext.PlanningSequenceSteps.AsNoTracking().Where(step => step.Id == stepId),
                sequence => sequence.Id,
                step => step.SequenceId,
                (sequence, step) => step.Id)
            .AnyAsync(cancellationToken);

    /// <summary>Whether the sequence's stored revision is still exactly the one the answer was proved at. A concurrent transition,
    /// a damaged revision or a missing or duplicated row is false, and nothing is retried.</summary>
    public static async Task<bool> IsRevisionUnchangedAsync(
        IDevalCopilotDbContext dbContext, Guid sequenceId, long revision, CancellationToken cancellationToken)
    {
        var after = await dbContext.PlanningSequences.AsNoTracking()
            .Where(candidate => candidate.Id == sequenceId)
            .Take(2)
            .ProjectRevision()
            .ToArrayAsync(cancellationToken);
        return after.Length == 1 && after[0] == revision;
    }

    private static async Task<PlanningSequenceInspectionStructure?> ProveAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        PlanningSequenceInspectionRunRow run,
        PlanningSequenceInspectionSequenceRow sequence,
        Guid sequenceId,
        CancellationToken cancellationToken)
    {
        var projectId = PlanningSequenceInspectionStorage.ParseIdentifier(run.ProjectId);
        var header = ProveHeader(sequence, sequenceId);
        if (RunExecutionModeStorage.Read(run.StoredMode) != RunExecutionMode.ManualAgent
            || projectId is not { } project
            || header is null
            || PlanningSequenceInspectionStorage.ParseIdentifier(sequence.RunId) != runId
            || PlanningSequenceInspectionStorage.ParseIdentifier(sequence.ProjectId) != project)
        {
            return null;
        }

        var rows = await dbContext.PlanningSequenceSteps.AsNoTracking()
            .Where(step => step.SequenceId == sequenceId)
            .OrderBy(step => step.Ordinal)
            .Take(PlanningSequenceInspectionProjection.StepBound)
            .ProjectStep()
            .ToArrayAsync(cancellationToken);
        var steps = ProveSteps(rows, sequenceId);
        if (steps is null || !await ProveReferencesAsync(dbContext, runId, project, header, steps, cancellationToken))
        {
            return null;
        }

        // The revision read with the header must still be the stored one: a concurrent transition during the bounded structural reads
        // withholds the whole answer. An explicit reload may read the later revision; nothing is retried here.
        return await IsRevisionUnchangedAsync(dbContext, sequenceId, header.Revision, cancellationToken)
            ? new PlanningSequenceInspectionStructure(runId, project, header, steps)
            : null;
    }

    private static PlanningSequenceInspectionHeader? ProveHeader(PlanningSequenceInspectionSequenceRow sequence, Guid sequenceId)
    {
        if (PlanningSequenceInspectionStorage.ParseIdentifier(sequence.WorkspaceId) is not { } workspaceId
            || PlanningSequenceInspectionStorage.ParseIdentifier(sequence.CheckpointId) is not { } checkpointId
            || !PlanningSequencePolicy.IsLowercaseSha256(sequence.Fingerprint)
            || sequence.StateCode is < 1 or > 9
            || sequence.Revision is not { } revision
            || sequence.ReasonOversized
            || PlanningSequenceInspectionStorage.ParseUtc(sequence.ArmedAtUtc) is not { } armedAtUtc
            || PlanningSequenceInspectionStorage.ParseUtc(sequence.UpdatedAtUtc) is not { } updatedAtUtc
            || updatedAtUtc < armedAtUtc)
        {
            return null;
        }

        var state = (PlanningSequenceState)(sequence.StateCode - 1);
        var ended = PlanningSequencePolicy.IsEnded(state);
        var stopRequestedAtUtc = PlanningSequenceInspectionStorage.ParseUtc(sequence.StopRequestedAtUtc);
        var endedAtUtc = PlanningSequenceInspectionStorage.ParseUtc(sequence.EndedAtUtc);

        // An ended sequence has a fixed reason and an end time within its lifetime; a live one, including Stopping, has neither.
        if (ended != sequence.EndedPresent
            || (ended ? !PlanningSequenceReason.IsKnown(sequence.ReasonCode) : sequence.ReasonCode is not null)
            || (ended && (endedAtUtc is null || endedAtUtc < armedAtUtc || endedAtUtc > updatedAtUtc)))
        {
            return null;
        }

        // A stop request exists only for Stopping and for an ended sequence, always for Stopping and Stopped, within the lifetime.
        var stopAllowed = ended || state == PlanningSequenceState.Stopping;
        var stopRequired = state is PlanningSequenceState.Stopping or PlanningSequenceState.Stopped;
        if ((sequence.StopRequestedPresent && (!stopAllowed || stopRequestedAtUtc is null || stopRequestedAtUtc < armedAtUtc || stopRequestedAtUtc > updatedAtUtc))
            || (stopRequired && !sequence.StopRequestedPresent))
        {
            return null;
        }

        return new PlanningSequenceInspectionHeader(
            sequenceId,
            workspaceId,
            checkpointId,
            sequence.Fingerprint!,
            state,
            sequence.ReasonCode,
            revision,
            armedAtUtc,
            updatedAtUtc,
            stopRequestedAtUtc,
            endedAtUtc);
    }

    private static PlanningSequenceInspectionStep[]? ProveSteps(PlanningSequenceInspectionStepRow[] rows, Guid sequenceId)
    {
        if (rows.Length > PlanningSequencePolicy.MaximumSteps)
        {
            return null;
        }

        var steps = new PlanningSequenceInspectionStep[rows.Length];
        var stepIds = new HashSet<Guid>();
        var reservedAttemptIds = new HashSet<Guid>();
        var attemptIds = new HashSet<Guid>();
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var role = PlanningSequencePolicy.RoleOf(index);
            var expectedRoleCode = role == AgentRole.Planner ? 1 : role == AgentRole.CriticalReviewer ? 2 : 3;
            if (row.Ordinal != index
                || row.RoleCode != expectedRoleCode
                || row.StateCode is < 1 or > 5
                || row.Revision is not { } revision
                || row.ReasonOversized
                || PlanningSequenceInspectionStorage.ParseIdentifier(row.SequenceId) != sequenceId
                || PlanningSequenceInspectionStorage.ParseIdentifier(row.Id) is not { } stepId
                || !stepIds.Add(stepId)
                || PlanningSequenceInspectionStorage.ParseIdentifier(row.ReservedAttemptId) is not { } reservedAttemptId
                || !reservedAttemptIds.Add(reservedAttemptId)
                || PlanningSequenceInspectionStorage.ParseUtc(row.PreparationReservedAtUtc) is not { } reservedAtUtc
                || PlanningSequenceInspectionStorage.ParseUtc(row.PreparationDeadlineUtc) is not { } deadlineUtc
                || PlanningSequenceInspectionStorage.ParseUtc(row.UpdatedAtUtc) is not { } updatedAtUtc
                || deadlineUtc <= reservedAtUtc
                || updatedAtUtc < reservedAtUtc)
            {
                return null;
            }

            var state = (PlanningSequenceStepState)(row.StateCode - 1);

            // Only the Planner has no predecessor and no input Proposal; every later step names exactly its predecessor's linked attempt.
            Guid? predecessorId = PlanningSequenceInspectionStorage.ParseIdentifier(row.PredecessorAttemptId);
            Guid? inputId = PlanningSequenceInspectionStorage.ParseIdentifier(row.InputProposalMessageId);
            if ((index == 0) == row.PredecessorPresent
                || (index == 0) == row.InputProposalPresent
                || (row.PredecessorPresent && predecessorId is null)
                || (row.InputProposalPresent && inputId is null)
                || (index > 0 && predecessorId != steps[index - 1].AttemptId)
                || (index > 0 && role == AgentRole.Resolver && inputId != steps[index - 1].InputProposalMessageId))
            {
                return null;
            }

            // Only a Claimed or Concluded step is linked, and exactly to the identity chosen at reservation; a Revoked or Unresolved one
            // carries a fixed reason and no other state has one.
            var linked = state is PlanningSequenceStepState.Claimed or PlanningSequenceStepState.Concluded;
            var reasoned = state is PlanningSequenceStepState.Revoked or PlanningSequenceStepState.Unresolved;
            Guid? attemptId = linked ? PlanningSequenceInspectionStorage.ParseIdentifier(row.AttemptId) : null;
            if (row.AttemptPresent != linked
                || (linked && (attemptId != reservedAttemptId || !attemptIds.Add(reservedAttemptId)))
                || (reasoned ? !PlanningSequenceReason.IsKnown(row.ReasonCode) : row.ReasonCode is not null)
                || (index < rows.Length - 1 && state != PlanningSequenceStepState.Concluded))
            {
                return null;
            }

            steps[index] = new PlanningSequenceInspectionStep(
                stepId,
                index,
                role,
                PlanningSequencePolicy.ProviderOf(index),
                PlanningSequencePolicy.ContractOf(index),
                state,
                row.ReasonCode,
                revision,
                predecessorId,
                inputId,
                attemptId,
                reservedAtUtc,
                deadlineUtc,
                updatedAtUtc);
        }

        return steps;
    }

    /// <summary>The recorded references by identity: the pinned workspace of the run's project, the pinned checkpoint in that workspace
    /// with the recorded fingerprint, and every named input as a Proposal of this run (a reviewer's attributed to its predecessor attempt).
    /// A missing, foreign, wrongly typed or oversized reference is incoherent; nothing is repaired from a latest row.</summary>
    private static async Task<bool> ProveReferencesAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        Guid projectId,
        PlanningSequenceInspectionHeader header,
        PlanningSequenceInspectionStep[] steps,
        CancellationToken cancellationToken)
    {
        var workspaceProjects = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(workspace => workspace.Id == header.WorkspaceId)
            .Take(2)
            .ProjectWorkspaceProject()
            .ToArrayAsync(cancellationToken);
        if (workspaceProjects.Length != 1 || PlanningSequenceInspectionStorage.ParseIdentifier(workspaceProjects[0]) != projectId)
        {
            return false;
        }

        var checkpoints = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(checkpoint => checkpoint.Id == header.CheckpointId)
            .Take(2)
            .ProjectCheckpoint()
            .ToArrayAsync(cancellationToken);
        if (checkpoints.Length != 1
            || checkpoints[0].FingerprintOversized
            || PlanningSequenceInspectionStorage.ParseIdentifier(checkpoints[0].WorkspaceId) != header.WorkspaceId
            || !string.Equals(checkpoints[0].Fingerprint, header.FingerprintSha256, StringComparison.Ordinal))
        {
            return false;
        }

        var proposalIds = steps.Where(step => step.InputProposalMessageId is not null)
            .Select(step => step.InputProposalMessageId!.Value).Distinct().ToArray();
        if (proposalIds.Length == 0)
        {
            return true;
        }

        var messages = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => proposalIds.Contains(message.Id))
            .Take(proposalIds.Length + 1)
            .ProjectMessage()
            .ToArrayAsync(cancellationToken);
        if (messages.Length != proposalIds.Length)
        {
            return false;
        }

        var proposals = new Dictionary<Guid, Guid?>();
        foreach (var message in messages)
        {
            if (PlanningSequenceInspectionStorage.ParseIdentifier(message.Id) is not { } messageId
                || PlanningSequenceInspectionStorage.ParseIdentifier(message.RunId) != runId
                || !message.IsProposal
                || (message.AttemptPresent && PlanningSequenceInspectionStorage.ParseIdentifier(message.AttemptId) is null)
                || !proposals.TryAdd(messageId, PlanningSequenceInspectionStorage.ParseIdentifier(message.AttemptId)))
            {
                return false;
            }
        }

        return proposalIds.All(proposals.ContainsKey)
            && steps.All(step => step.Role != AgentRole.CriticalReviewer
                || (step.InputProposalMessageId is { } input && proposals[input] is { } author && author == step.PredecessorAttemptId));
    }
}
