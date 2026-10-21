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

        // The bounded binding candidates and the run's bounded event candidates are read in ONE statement, so a competing binding write
        // (an atomic binding-and-event pair) is seen as both rows or neither and never as a mixed reading.
        var bindingCandidates = dbContext.GitHubRepositoryIdentityBindings.AsNoTracking()
            .Where(candidate => candidate.RunId == runId
                || (operationId != null && candidate.LocalCommitOperationId == operationId)
                || (targetId != null && candidate.ObservationTargetId == targetId))
            .Select(candidate => new PairRow
            {
                IsBinding = true,
                Id = candidate.Id,
                RunId = candidate.RunId,
                ProjectId = candidate.ProjectId,
                LocalCommitOperationId = candidate.LocalCommitOperationId,
                GitWorkspaceId = candidate.GitWorkspaceId,
                ObservationTargetId = candidate.ObservationTargetId,
                CommitSha = ((byte[])(object)candidate.CommitSha).Length <= CommitBytes ? candidate.CommitSha : null,
                BranchName = ((byte[])(object)candidate.BranchName).Length <= BranchBytes ? candidate.BranchName : null,
                RepositoryNodeId = ((byte[])(object)candidate.RepositoryNodeId).Length <= NodeIdBytes ? candidate.RepositoryNodeId : null,
                Owner = ((byte[])(object)candidate.Owner).Length <= OwnerBytes ? candidate.Owner : null,
                Repository = ((byte[])(object)candidate.Repository).Length <= RepositoryBytes ? candidate.Repository : null,
                ObservedAtUtc = candidate.ObservedAtUtc,
                RecordedAtUtc = candidate.RecordedAtUtc,
                EventId = candidate.EventId,
                IsHumanShape = false,
                OccurredAtUtc = null,
                Payload = null,
            })
            .Take(3);
        var eventCandidates = dbContext.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.EventType == RunEventType.GitHubRepositoryIdentityBound)
            .Select(candidate => new PairRow
            {
                IsBinding = false,
                Id = candidate.Id,
                RunId = null,
                ProjectId = null,
                LocalCommitOperationId = null,
                GitWorkspaceId = null,
                ObservationTargetId = null,
                CommitSha = null,
                BranchName = null,
                RepositoryNodeId = null,
                Owner = null,
                Repository = null,
                ObservedAtUtc = null,
                RecordedAtUtc = null,
                EventId = null,
                // Classified in the database, never materialized: an actor value this version does not know is an incoherent reading,
                // not an EF conversion fault. Only the exact Human shape (no attempt, role or provider) is coherent.
                IsHumanShape = candidate.AttemptId == null && candidate.ActorKind == ParticipantKind.Human
                    && candidate.ActorAgentRole == null && candidate.ActorAgentProvider == null,
                OccurredAtUtc = candidate.OccurredAtUtc,
                Payload = ((byte[])(object)candidate.PayloadJson).Length <= GitHubRepositoryIdentityBindingEventPayload.MaximumLength
                    ? candidate.PayloadJson
                    : null,
            })
            .Take(3);
        var pair = await bindingCandidates.Concat(eventCandidates).ToListAsync(cancellationToken);
        var rows = pair.Where(candidate => candidate.IsBinding).ToList();
        var events = pair.Where(candidate => !candidate.IsBinding).ToList();

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
            || row.ProjectId is null || row.LocalCommitOperationId is null || row.GitWorkspaceId is null || row.ObservationTargetId is null
            || row.ObservedAtUtc is null || row.RecordedAtUtc is null || row.EventId is null
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
                row.Id, runId, row.ProjectId.Value, row.LocalCommitOperationId.Value, row.GitWorkspaceId.Value, row.ObservationTargetId.Value,
                row.CommitSha, row.BranchName, row.RepositoryNodeId, row.Owner, row.Repository, target.RecordedAtUtc, row.ObservedAtUtc.Value,
                row.RecordedAtUtc.Value, row.EventId.Value);
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
    /// <summary>One candidate of the combined read: a binding row or a bound-event row, told apart by <see cref="IsBinding"/>; the
    /// members of the other kind are null.</summary>
    private sealed class PairRow
    {
        public bool IsBinding { get; init; }

        public Guid Id { get; init; }

        public Guid? RunId { get; init; }

        public Guid? ProjectId { get; init; }

        public Guid? LocalCommitOperationId { get; init; }

        public Guid? GitWorkspaceId { get; init; }

        public Guid? ObservationTargetId { get; init; }

        public string? CommitSha { get; init; }

        public string? BranchName { get; init; }

        public string? RepositoryNodeId { get; init; }

        public string? Owner { get; init; }

        public string? Repository { get; init; }

        public DateTimeOffset? ObservedAtUtc { get; init; }

        public DateTimeOffset? RecordedAtUtc { get; init; }

        public Guid? EventId { get; init; }

        public bool IsHumanShape { get; init; }

        public DateTimeOffset? OccurredAtUtc { get; init; }

        public string? Payload { get; init; }
    }
}
