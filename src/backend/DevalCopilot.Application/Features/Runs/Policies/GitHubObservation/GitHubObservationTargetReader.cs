using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

/// <summary>
/// Reads a run's recorded GitHub observation target through bounded untracked projections and validates it together with the
/// source it was recorded against and its one Human event (ADR-0040). A target is coherent only when its entity invariants hold,
/// every pinned identity, commit and branch equals the source, the run owns exactly one event of the recorded type, that event is the
/// target's own Human event with the same time and the exact canonical version-1 payload. Everything else is incoherent: it is never
/// repaired, defaulted or replayed as a success. Nothing is written.
/// </summary>
internal static class GitHubObservationTargetReader
{
    // length() of a TEXT value stops at an embedded NUL and HasMaxLength does not bound SQLite storage, so every stored text is bounded by
    // its stored bytes (CAST AS BLOB) before it is materialized. The supported names are ASCII, so their byte bounds are their character
    // bounds; the branch allows up to three UTF-8 bytes per unit of its Domain bound. A longer stored value is simply not a record.
    private const int CommitBytes = 40;
    private const int OwnerBytes = GitHubRepositoryNamePolicy.MaximumOwnerLength;
    private const int RepositoryBytes = GitHubRepositoryNamePolicy.MaximumRepositoryLength;
    private const int BranchBytes = GitHubObservationTarget.MaximumBranchNameLength * 3;

    public static async Task<GitHubObservationTargetReading> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid runId, GitHubObservationSource? source, CancellationToken cancellationToken)
    {
        Guid? operationId = source?.OperationId;
        var rows = await dbContext.GitHubObservationTargets.AsNoTracking()
            .Where(candidate => candidate.RunId == runId || (operationId != null && candidate.LocalCommitOperationId == operationId))
            .Select(candidate => new
            {
                candidate.Id,
                candidate.RunId,
                candidate.ProjectId,
                candidate.LocalCommitOperationId,
                candidate.GitWorkspaceId,
                CommitSha = ((byte[])(object)candidate.CommitSha).Length <= CommitBytes ? candidate.CommitSha : null,
                BranchName = ((byte[])(object)candidate.BranchName).Length <= BranchBytes ? candidate.BranchName : null,
                Owner = ((byte[])(object)candidate.Owner).Length <= OwnerBytes ? candidate.Owner : null,
                Repository = ((byte[])(object)candidate.Repository).Length <= RepositoryBytes ? candidate.Repository : null,
                candidate.RecordedAtUtc,
                candidate.EventId,
            })
            .Take(3)
            .ToListAsync(cancellationToken);

        var events = await dbContext.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.EventType == RunEventType.GitHubObservationTargetRecorded)
            .Select(candidate => new
            {
                candidate.Id,
                // Classified in the database, never materialized: a stored actor value this version does not know must be an incoherent
                // reading, not an EF conversion fault. Only the exact Human shape (no attempt, role or provider) is coherent.
                IsHumanShape = candidate.AttemptId == null && candidate.ActorKind == ParticipantKind.Human
                    && candidate.ActorAgentRole == null && candidate.ActorAgentProvider == null,
                candidate.OccurredAtUtc,
                Payload = ((byte[])(object)candidate.PayloadJson).Length <= GitHubObservationTargetEventPayload.MaximumLength
                    ? candidate.PayloadJson
                    : null,
            })
            .Take(3)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return events.Count == 0 ? GitHubObservationTargetReading.None : GitHubObservationTargetReading.Incoherent;
        }

        if (rows.Count != 1 || events.Count != 1 || source is null)
        {
            return GitHubObservationTargetReading.Incoherent;
        }

        var row = rows[0];
        var recordedEvent = events[0];
        if (row.RunId != runId || row.ProjectId != source.ProjectId || row.LocalCommitOperationId != source.OperationId
            || row.GitWorkspaceId != source.WorkspaceId || !string.Equals(row.CommitSha, source.CommitSha, StringComparison.Ordinal)
            || !string.Equals(row.BranchName, source.BranchName, StringComparison.Ordinal)
            || row.CommitSha is null || row.BranchName is null || row.Owner is null || row.Repository is null
            || recordedEvent.Id != row.EventId || !recordedEvent.IsHumanShape
            || recordedEvent.OccurredAtUtc != row.RecordedAtUtc || recordedEvent.Payload is null)
        {
            return GitHubObservationTargetReading.Incoherent;
        }

        GitHubObservationTarget target;
        try
        {
            target = GitHubObservationTarget.Record(
                row.Id, row.RunId, row.ProjectId, row.LocalCommitOperationId, row.GitWorkspaceId, row.CommitSha, row.BranchName,
                row.Owner, row.Repository, row.RecordedAtUtc, row.EventId);
        }
        catch (ArgumentException)
        {
            return GitHubObservationTargetReading.Incoherent;
        }

        return string.Equals(recordedEvent.Payload, GitHubObservationTargetEventPayload.Build(target), StringComparison.Ordinal)
            ? new GitHubObservationTargetReading(
                GitHubObservationTargetReadingKind.Coherent,
                new GitHubObservationTargetView(
                    target.Id, target.EventId, GitHubObservationTarget.Host, target.Owner, target.Repository, target.RecordedAtUtc))
            : GitHubObservationTargetReading.Incoherent;
    }
}
