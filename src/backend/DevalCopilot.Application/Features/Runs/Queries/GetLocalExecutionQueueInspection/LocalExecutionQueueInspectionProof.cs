using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;

/// <summary>
/// The whole proof of one global outstanding-admission snapshot (ADR-0064), over the rows of one statement and nothing else. Each
/// candidate's rows are proved by the unchanged <see cref="LocalExecutionAdmissionHistoryProof"/> (the whole ledger, its single root and
/// every historical reference) and the candidate must be exactly the proved last admission of its queued sequence, in an outstanding
/// state (Waiting, Reserved or Unresolved) that the sequence's recorded state allows. The candidates are distinct, in strictly
/// increasing admission order, at most 64 of them on a sequence that has not ended and at most four held, with one per sequence and
/// run, and each one's registered project is the run's own with a nonblank name of at most 200 units. Any contradiction, overflow or
/// missing reference makes the whole answer Unavailable, never a partial list or count. Recorded bookkeeping only: nothing here asks
/// whether an admission could be reserved now, whether any process lives, or whether capacity is free.
/// </summary>
internal static class LocalExecutionQueueInspectionProof
{
    private const int MaximumProjectNameLength = 200;

    private static readonly GetLocalExecutionQueueInspectionQueryResult Empty =
        new(LocalExecutionQueueInspectionStatus.Empty, []);

    private static readonly GetLocalExecutionQueueInspectionQueryResult Unavailable =
        new(LocalExecutionQueueInspectionStatus.Unavailable, []);

    public static GetLocalExecutionQueueInspectionQueryResult Prove(LocalExecutionQueueInspectionRow[] rows)
    {
        if (rows.Length == 0)
        {
            return Empty;
        }

        if (rows.Length >= LocalExecutionQueueInspectionStatement.RowCeiling)
        {
            return Unavailable;
        }

        var entries = new List<LocalExecutionQueueInspectionEntry>();
        var candidateIds = new HashSet<Guid>();
        var sequenceIds = new HashSet<Guid>();
        var runIds = new HashSet<Guid>();
        long previousNumber = 0;
        var liveSequences = 0;
        var heldAdmissions = 0;
        for (var start = 0; start < rows.Length;)
        {
            var candidate = rows[start].Candidate;
            var end = start;
            while (end < rows.Length && rows[end].Candidate == candidate)
            {
                end++;
            }

            if (entries.Count >= LocalExecutionQueueInspectionStatement.CandidateBound - 1
                || candidate.Id is not { } id || id == Guid.Empty || !candidateIds.Add(id)
                || candidate.SequenceId is not { } sequenceId || sequenceId == Guid.Empty || !sequenceIds.Add(sequenceId)
                || candidate.Number is not { } number || number <= previousNumber
                || candidate.StateCode is not (1 or 2 or 5))
            {
                return Unavailable;
            }

            previousNumber = number;
            var group = rows[start..end];
            var entry = ProveCandidate(group, candidate, id, sequenceId, number);
            if (entry is null || !runIds.Add(entry.RunId))
            {
                return Unavailable;
            }

            liveSequences += LocalExecutionSequencePolicy.IsEnded(entry.Sequence.RecordedState) ? 0 : 1;
            heldAdmissions += candidate.StateCode is 2 or 5 ? 1 : 0;
            if (liveSequences > LocalExecutionSequencePolicy.MaximumQueuedCycles
                || heldAdmissions > LocalExecutionSequencePolicy.MaximumHeldPreparations)
            {
                return Unavailable;
            }

            entries.Add(entry);
            start = end;
        }

        return new GetLocalExecutionQueueInspectionQueryResult(LocalExecutionQueueInspectionStatus.Recorded, entries);
    }

    private static LocalExecutionQueueInspectionEntry? ProveCandidate(
        LocalExecutionQueueInspectionRow[] group,
        LocalExecutionQueueInspectionCandidate candidate,
        Guid candidateId,
        Guid sequenceId,
        long number)
    {
        var project = group[0].Project;
        var history = group.Select(row => row.History).ToArray();
        var root = history[0].Root;
        if (group.Any(row => row.Project != project)
            || project.Id is not { } projectId || projectId == Guid.Empty || projectId != root.RunProjectId
            || string.IsNullOrWhiteSpace(project.Name) || project.Name.Length > MaximumProjectNameLength)
        {
            return null;
        }

        var reading = LocalExecutionAdmissionHistoryProof.Prove(history);
        if (reading.Kind != LocalExecutionAdmissionHistoryReadingKind.Recorded || reading.Header is not { } header
            || header.SequenceId != sequenceId)
        {
            return null;
        }

        var last = reading.Admissions[^1];
        var expected = candidate.StateCode switch
        {
            1 => LocalExecutionAdmissionState.Waiting,
            2 => LocalExecutionAdmissionState.Reserved,
            _ => LocalExecutionAdmissionState.Unresolved,
        };
        return last.AdmissionId == candidateId && last.AdmissionNumber == number && last.RecordedState == expected
            ? new LocalExecutionQueueInspectionEntry(projectId, project.Name, root.RunId, header, last)
            : null;
    }
}
