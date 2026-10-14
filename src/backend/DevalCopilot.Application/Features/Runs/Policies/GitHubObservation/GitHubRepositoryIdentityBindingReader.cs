using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

/// <summary>
/// Reads a run's recorded identity binding through bounded untracked projections and validates it together with the source and target it was
/// recorded against and its one Human event (ADR-0043). A binding is coherent only when its entity invariants hold (including the UTC order
/// of target, observation and binding), every pinned identity, commit and branch equals the source, it names the coherent target, its names
/// equal the target's ignoring only ASCII case, the run owns exactly one event of the recorded type, and that event is the binding's own Human
/// event with the same time and the exact canonical version-1 payload. Everything else is incoherent: never repaired, defaulted or replayed
/// as a success, and a binding or event without a coherent target is an orphan, not an absent record. Nothing is written.
/// </summary>
internal static class GitHubRepositoryIdentityBindingReader
{
    // See GitHubObservationTargetReader: every stored text is bounded by its stored bytes before it is materialized.
    private const int CommitBytes = 40;
    private const int OwnerBytes = GitHubRepositoryNamePolicy.MaximumOwnerLength;
    private const int RepositoryBytes = GitHubRepositoryNamePolicy.MaximumRepositoryLength;
    private const int BranchBytes = GitHubObservationTarget.MaximumBranchNameLength * 3;
    private const int NodeIdBytes = GitHubRepositoryNodeIdPolicy.MaximumLength;

    public static async Task<GitHubRepositoryIdentityBindingReading> ReadAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        GitHubObservationSource? source,
        GitHubObservationTargetView? target,
        CancellationToken cancellationToken)
    {
        Guid? operationId = source?.OperationId;
        Guid? targetId = target?.Id;
        var rows = await dbContext.GitHubRepositoryIdentityBindings.AsNoTracking()
            .Where(candidate => candidate.RunId == runId
                || (operationId != null && candidate.LocalCommitOperationId == operationId)
                || (targetId != null && candidate.ObservationTargetId == targetId))
            .Select(candidate => new
            {
                candidate.Id,
                candidate.RunId,
                candidate.ProjectId,
                candidate.LocalCommitOperationId,
                candidate.GitWorkspaceId,
                candidate.ObservationTargetId,
                CommitSha = ((byte[])(object)candidate.CommitSha).Length <= CommitBytes ? candidate.CommitSha : null,
                BranchName = ((byte[])(object)candidate.BranchName).Length <= BranchBytes ? candidate.BranchName : null,
                RepositoryNodeId = ((byte[])(object)candidate.RepositoryNodeId).Length <= NodeIdBytes ? candidate.RepositoryNodeId : null,
                Owner = ((byte[])(object)candidate.Owner).Length <= OwnerBytes ? candidate.Owner : null,
                Repository = ((byte[])(object)candidate.Repository).Length <= RepositoryBytes ? candidate.Repository : null,
                candidate.ObservedAtUtc,
                candidate.RecordedAtUtc,
                candidate.EventId,
            })
            .Take(3)
            .ToListAsync(cancellationToken);

        var events = await dbContext.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.EventType == RunEventType.GitHubRepositoryIdentityBound)
            .Select(candidate => new
            {
                candidate.Id,
                // Classified in the database, never materialized: an actor value this version does not know is an incoherent reading,
                // not an EF conversion fault. Only the exact Human shape (no attempt, role or provider) is coherent.
                IsHumanShape = candidate.AttemptId == null && candidate.ActorKind == ParticipantKind.Human
                    && candidate.ActorAgentRole == null && candidate.ActorAgentProvider == null,
                candidate.OccurredAtUtc,
                Payload = ((byte[])(object)candidate.PayloadJson).Length <= GitHubRepositoryIdentityBindingEventPayload.MaximumLength
                    ? candidate.PayloadJson
                    : null,
            })
            .Take(3)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return events.Count == 0 ? GitHubRepositoryIdentityBindingReading.None : GitHubRepositoryIdentityBindingReading.Incoherent;
        }

        if (rows.Count != 1 || events.Count != 1 || source is null || target is null)
        {
            return GitHubRepositoryIdentityBindingReading.Incoherent;
        }

        var row = rows[0];
        var recordedEvent = events[0];
        if (row.RunId != runId || row.ProjectId != source.ProjectId || row.LocalCommitOperationId != source.OperationId
            || row.GitWorkspaceId != source.WorkspaceId || row.ObservationTargetId != target.Id
            || row.CommitSha is null || row.BranchName is null || row.RepositoryNodeId is null || row.Owner is null || row.Repository is null
            || !string.Equals(row.CommitSha, source.CommitSha, StringComparison.Ordinal)
            || !string.Equals(row.BranchName, source.BranchName, StringComparison.Ordinal)
            || !string.Equals(row.Owner, target.Owner, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(row.Repository, target.Repository, StringComparison.OrdinalIgnoreCase)
            || recordedEvent.Id != row.EventId || !recordedEvent.IsHumanShape
            || recordedEvent.OccurredAtUtc != row.RecordedAtUtc || recordedEvent.Payload is null)
        {
            return GitHubRepositoryIdentityBindingReading.Incoherent;
        }

        GitHubRepositoryIdentityBinding binding;
        try
        {
            binding = GitHubRepositoryIdentityBinding.Record(
                row.Id, row.RunId, row.ProjectId, row.LocalCommitOperationId, row.GitWorkspaceId, row.ObservationTargetId, row.CommitSha,
                row.BranchName, row.RepositoryNodeId, row.Owner, row.Repository, target.RecordedAtUtc, row.ObservedAtUtc, row.RecordedAtUtc,
                row.EventId);
        }
        catch (ArgumentException)
        {
            return GitHubRepositoryIdentityBindingReading.Incoherent;
        }

        return string.Equals(recordedEvent.Payload, GitHubRepositoryIdentityBindingEventPayload.Build(binding), StringComparison.Ordinal)
            ? new GitHubRepositoryIdentityBindingReading(
                GitHubRepositoryIdentityBindingReadingKind.Coherent,
                new GitHubRepositoryIdentityBindingView(
                    binding.Id, binding.EventId, binding.RepositoryNodeId, binding.Owner, binding.Repository, binding.ObservedAtUtc,
                    binding.RecordedAtUtc))
            : GitHubRepositoryIdentityBindingReading.Incoherent;
    }
}
