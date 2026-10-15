using System.Globalization;
using System.Text;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

// The folder namespace has the entity's name, so the entity is named through an alias inside it.
using Approval = DevalCopilot.Domain.Features.Runs.GitHubBranchPublicationApproval;

namespace DevalCopilot.Application.Features.Runs.Policies.GitHubBranchPublicationApproval;

/// <summary>
/// Reads a run's recorded publication approval through bounded untracked projections and validates it together with the whole scope it
/// was recorded against and its one Human event (ADR-0048). An approval is coherent only when its entity invariants hold (including the
/// UTC order of target, observation, binding and approval), every pinned identity, commit, branch, target and binding snapshot equals the
/// scope reading exactly, the run owns exactly one event of the recorded type, and that event is the approval's own Human event with the
/// same time and the exact canonical version-1 payload. Rows and events are detected independently, including rows of other runs that name
/// this run's operation, target or binding. Everything else is incoherent: never repaired, defaulted or replayed as a success, and a record
/// or event without a whole coherent scope is an orphan, not an absent record. Every stored value, identifiers and times included, is
/// bounded by its stored bytes in the database and admitted from that raw representation before it is parsed, so an oversized, malformed
/// or NUL-containing value is an incoherent reading and never a conversion fault. The rows and the events are the two bounded operands of
/// one database statement, so they are one consistent snapshot: a competing writer's atomic commit of a row and its event is seen whole or
/// not at all, never as an event without its row. Nothing is written.
/// </summary>
internal static class GitHubBranchPublicationApprovalReader
{
    // See GitHubObservationTargetReader: every stored text is bounded by its stored bytes before it is materialized. The supported names
    // are ASCII, so their byte bounds are their character bounds, and the branch an approval keeps is printable ASCII.
    private const int CommitBytes = 40;
    private const int OwnerBytes = GitHubRepositoryNamePolicy.MaximumOwnerLength;
    private const int RepositoryBytes = GitHubRepositoryNamePolicy.MaximumRepositoryLength;
    private const int BranchBytes = GitHubObservationTarget.MaximumBranchNameLength;
    private const int NodeIdBytes = GitHubRepositoryNodeIdPolicy.MaximumLength;

    // The stored identifier is the 36-character D-format text and the stored time is the provider writer's text
    // (yyyy-MM-dd HH:mm:ss.FFFFFFFzzz, at most 33 characters). Both are ASCII, so the byte bound is the character bound.
    private const int IdentifierBytes = 36;
    private const int TimeBytes = 33;
    private const string StoredTimeFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz";

    public static async Task<GitHubBranchPublicationApprovalReading> ReadAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        GitHubBranchPublicationScopeReading scopeReading,
        CancellationToken cancellationToken)
    {
        Guid? operationId = scopeReading.Source?.OperationId;
        Guid? targetId = scopeReading.CoherentTarget?.Id;
        Guid? bindingId = scopeReading.CoherentBinding?.Id;
        // One statement, one snapshot: the approval rows and the run's approval events are two bounded operands of a single UNION ALL, so a
        // competing writer's atomic row-and-event commit is seen whole or not at all. Two statements could straddle that commit and read an
        // event without its row.
        var approvalRows = dbContext.GitHubBranchPublicationApprovals.AsNoTracking()
            .Where(candidate => candidate.RunId == runId
                || (operationId != null && candidate.LocalCommitOperationId == operationId)
                || (targetId != null && candidate.TargetId == targetId)
                || (bindingId != null && candidate.BindingId == bindingId))
            .Select(candidate => new StoredEvidence
            {
                IsRow = true,
                // Identifiers and times are projected as their raw stored bytes (a cast to a blob never parses them) and withheld as a
                // null above their bound; the admission below parses the bounded bytes.
                Id = ((byte[])(object)candidate.Id).Length <= IdentifierBytes ? (byte[])(object)candidate.Id : null,
                EventId = ((byte[])(object)candidate.EventId).Length <= IdentifierBytes ? (byte[])(object)candidate.EventId : null,
                RunId = ((byte[])(object)candidate.RunId).Length <= IdentifierBytes ? (byte[])(object)candidate.RunId : null,
                ProjectId = ((byte[])(object)candidate.ProjectId).Length <= IdentifierBytes ? (byte[])(object)candidate.ProjectId : null,
                LocalCommitOperationId = ((byte[])(object)candidate.LocalCommitOperationId).Length <= IdentifierBytes
                    ? (byte[])(object)candidate.LocalCommitOperationId
                    : null,
                GitWorkspaceId = ((byte[])(object)candidate.GitWorkspaceId).Length <= IdentifierBytes
                    ? (byte[])(object)candidate.GitWorkspaceId
                    : null,
                CommitSha = ((byte[])(object)candidate.CommitSha).Length <= CommitBytes ? candidate.CommitSha : null,
                BranchName = ((byte[])(object)candidate.BranchName).Length <= BranchBytes ? candidate.BranchName : null,
                TargetId = ((byte[])(object)candidate.TargetId).Length <= IdentifierBytes ? (byte[])(object)candidate.TargetId : null,
                TargetEventId = ((byte[])(object)candidate.TargetEventId).Length <= IdentifierBytes
                    ? (byte[])(object)candidate.TargetEventId
                    : null,
                TargetOwner = ((byte[])(object)candidate.TargetOwner).Length <= OwnerBytes ? candidate.TargetOwner : null,
                TargetRepository = ((byte[])(object)candidate.TargetRepository).Length <= RepositoryBytes ? candidate.TargetRepository : null,
                TargetRecordedAtUtc = ((byte[])(object)candidate.TargetRecordedAtUtc).Length <= TimeBytes
                    ? (byte[])(object)candidate.TargetRecordedAtUtc
                    : null,
                BindingId = ((byte[])(object)candidate.BindingId).Length <= IdentifierBytes ? (byte[])(object)candidate.BindingId : null,
                BindingEventId = ((byte[])(object)candidate.BindingEventId).Length <= IdentifierBytes
                    ? (byte[])(object)candidate.BindingEventId
                    : null,
                RepositoryNodeId = ((byte[])(object)candidate.RepositoryNodeId).Length <= NodeIdBytes ? candidate.RepositoryNodeId : null,
                BindingOwner = ((byte[])(object)candidate.BindingOwner).Length <= OwnerBytes ? candidate.BindingOwner : null,
                BindingRepository = ((byte[])(object)candidate.BindingRepository).Length <= RepositoryBytes
                    ? candidate.BindingRepository
                    : null,
                BindingObservedAtUtc = ((byte[])(object)candidate.BindingObservedAtUtc).Length <= TimeBytes
                    ? (byte[])(object)candidate.BindingObservedAtUtc
                    : null,
                BindingRecordedAtUtc = ((byte[])(object)candidate.BindingRecordedAtUtc).Length <= TimeBytes
                    ? (byte[])(object)candidate.BindingRecordedAtUtc
                    : null,
                ApprovedAtUtc = ((byte[])(object)candidate.ApprovedAtUtc).Length <= TimeBytes
                    ? (byte[])(object)candidate.ApprovedAtUtc
                    : null,
                IsHumanShape = null,
                OccurredAtUtc = null,
                Payload = null,
            })
            .Take(3);

        var approvalEvents = dbContext.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.EventType == RunEventType.GitHubBranchPublicationApproved)
            .Select(candidate => new StoredEvidence
            {
                IsRow = false,
                Id = ((byte[])(object)candidate.Id).Length <= IdentifierBytes ? (byte[])(object)candidate.Id : null,
                EventId = null,
                RunId = null,
                ProjectId = null,
                LocalCommitOperationId = null,
                GitWorkspaceId = null,
                CommitSha = null,
                BranchName = null,
                TargetId = null,
                TargetEventId = null,
                TargetOwner = null,
                TargetRepository = null,
                TargetRecordedAtUtc = null,
                BindingId = null,
                BindingEventId = null,
                RepositoryNodeId = null,
                BindingOwner = null,
                BindingRepository = null,
                BindingObservedAtUtc = null,
                BindingRecordedAtUtc = null,
                ApprovedAtUtc = null,
                // Classified in the database, never materialized: an actor value this version does not know is an incoherent reading,
                // not an EF conversion fault. Only the exact Human shape (no attempt, role or provider) is coherent.
                IsHumanShape = candidate.AttemptId == null && candidate.ActorKind == ParticipantKind.Human
                    && candidate.ActorAgentRole == null && candidate.ActorAgentProvider == null,
                OccurredAtUtc = ((byte[])(object)candidate.OccurredAtUtc).Length <= TimeBytes
                    ? (byte[])(object)candidate.OccurredAtUtc
                    : null,
                Payload = ((byte[])(object)candidate.PayloadJson).Length <= GitHubBranchPublicationApprovalEventPayload.MaximumLength
                    ? candidate.PayloadJson
                    : null,
            })
            .Take(3);

        var evidence = await approvalRows.Concat(approvalEvents).ToListAsync(cancellationToken);
        var rows = evidence.Where(candidate => candidate.IsRow).ToList();
        var events = evidence.Where(candidate => !candidate.IsRow).ToList();

        if (rows.Count == 0)
        {
            return events.Count == 0 ? GitHubBranchPublicationApprovalReading.None : GitHubBranchPublicationApprovalReading.Incoherent;
        }

        if (rows.Count != 1 || events.Count != 1 || scopeReading.Scope is not { } scope)
        {
            return GitHubBranchPublicationApprovalReading.Incoherent;
        }

        var row = rows[0];
        var recordedEvent = events[0];
        if (ParseIdentifier(row.Id) is not { } id || ParseIdentifier(row.EventId) is not { } eventId
            || ParseIdentifier(row.RunId) is not { } rowRunId || ParseIdentifier(row.ProjectId) is not { } projectId
            || ParseIdentifier(row.LocalCommitOperationId) is not { } rowOperationId
            || ParseIdentifier(row.GitWorkspaceId) is not { } workspaceId
            || ParseIdentifier(row.TargetId) is not { } rowTargetId || ParseIdentifier(row.TargetEventId) is not { } targetEventId
            || ParseTime(row.TargetRecordedAtUtc) is not { } targetRecordedAtUtc
            || ParseIdentifier(row.BindingId) is not { } rowBindingId || ParseIdentifier(row.BindingEventId) is not { } bindingEventId
            || ParseTime(row.BindingObservedAtUtc) is not { } bindingObservedAtUtc
            || ParseTime(row.BindingRecordedAtUtc) is not { } bindingRecordedAtUtc
            || ParseTime(row.ApprovedAtUtc) is not { } approvedAtUtc
            || ParseIdentifier(recordedEvent.Id) is not { } recordedEventId
            || ParseTime(recordedEvent.OccurredAtUtc) is not { } occurredAtUtc)
        {
            return GitHubBranchPublicationApprovalReading.Incoherent;
        }

        var source = scope.Source;
        var target = scope.Target;
        var binding = scope.Binding;
        if (rowRunId != runId || projectId != source.ProjectId || rowOperationId != source.OperationId
            || workspaceId != source.WorkspaceId
            || row.CommitSha is null || row.BranchName is null || row.TargetOwner is null || row.TargetRepository is null
            || row.RepositoryNodeId is null || row.BindingOwner is null || row.BindingRepository is null
            || !string.Equals(row.CommitSha, source.CommitSha, StringComparison.Ordinal)
            || !string.Equals(row.BranchName, source.BranchName, StringComparison.Ordinal)
            || rowTargetId != target.Id || targetEventId != target.EventId
            || !string.Equals(row.TargetOwner, target.Owner, StringComparison.Ordinal)
            || !string.Equals(row.TargetRepository, target.Repository, StringComparison.Ordinal)
            || targetRecordedAtUtc != target.RecordedAtUtc
            || rowBindingId != binding.Id || bindingEventId != binding.EventId
            || !string.Equals(row.RepositoryNodeId, binding.RepositoryNodeId, StringComparison.Ordinal)
            || !string.Equals(row.BindingOwner, binding.Owner, StringComparison.Ordinal)
            || !string.Equals(row.BindingRepository, binding.Repository, StringComparison.Ordinal)
            || bindingObservedAtUtc != binding.ObservedAtUtc || bindingRecordedAtUtc != binding.RecordedAtUtc
            || recordedEventId != eventId || recordedEvent.IsHumanShape != true
            || occurredAtUtc != approvedAtUtc || recordedEvent.Payload is null)
        {
            return GitHubBranchPublicationApprovalReading.Incoherent;
        }

        Approval approval;
        try
        {
            approval = Approval.Record(
                id, eventId, rowRunId, projectId, rowOperationId, workspaceId, row.CommitSha,
                row.BranchName, rowTargetId, targetEventId, row.TargetOwner, row.TargetRepository, targetRecordedAtUtc,
                rowBindingId, bindingEventId, row.RepositoryNodeId, row.BindingOwner, row.BindingRepository,
                bindingObservedAtUtc, bindingRecordedAtUtc, approvedAtUtc);
        }
        catch (ArgumentException)
        {
            return GitHubBranchPublicationApprovalReading.Incoherent;
        }

        return string.Equals(recordedEvent.Payload, GitHubBranchPublicationApprovalEventPayload.Build(approval), StringComparison.Ordinal)
            ? new GitHubBranchPublicationApprovalReading(GitHubBranchPublicationApprovalReadingKind.Coherent, ToView(approval))
            : GitHubBranchPublicationApprovalReading.Incoherent;
    }

    public static GitHubBranchPublicationApprovalView ToView(Approval approval) => new(
        approval.Id,
        approval.EventId,
        Approval.Host,
        Approval.Action,
        Approval.PolicyVersion,
        approval.LocalCommitOperationId,
        approval.GitWorkspaceId,
        approval.TargetId,
        approval.BindingId,
        approval.RepositoryNodeId,
        approval.TargetOwner,
        approval.TargetRepository,
        approval.BranchName,
        approval.CommitSha,
        approval.ApprovedAtUtc);

    // The only supported stored identifier is the exact 36-character D-format text of a non-empty identifier. Nothing is trimmed, braced,
    // unhyphenated or otherwise normalized, and a withheld (oversized), NUL-containing, non-ASCII or non-text value is not an identifier.
    private static Guid? ParseIdentifier(byte[]? stored)
    {
        if (stored is not { Length: IdentifierBytes } || !IsPrintableAscii(stored))
        {
            return null;
        }

        var text = Encoding.ASCII.GetString(stored);
        return Guid.TryParseExact(text, "D", out var identifier) && identifier != Guid.Empty ? identifier : null;
    }

    // The only supported stored time is the provider writer's text: a space-separated local date and time, optional fractional seconds
    // and a numeric offset. Nothing is trimmed or normalized, and the entity decides whether the offset is the required UTC.
    private static DateTimeOffset? ParseTime(byte[]? stored)
    {
        if (stored is not { Length: > 0 and <= TimeBytes } || !IsPrintableAscii(stored))
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(
            Encoding.ASCII.GetString(stored), StoredTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;
    }

    private static bool IsPrintableAscii(byte[] stored) => stored.All(value => value is >= 0x20 and <= 0x7E);

    // One shape for both operands of the single statement: the approval row's columns and the approval event's columns, each side leaving
    // the other side's members null. Raw bounded bytes and texts only; nothing here has been parsed.
    private sealed class StoredEvidence
    {
        public bool IsRow { get; init; }

        public byte[]? Id { get; init; }

        public byte[]? EventId { get; init; }

        public byte[]? RunId { get; init; }

        public byte[]? ProjectId { get; init; }

        public byte[]? LocalCommitOperationId { get; init; }

        public byte[]? GitWorkspaceId { get; init; }

        public string? CommitSha { get; init; }

        public string? BranchName { get; init; }

        public byte[]? TargetId { get; init; }

        public byte[]? TargetEventId { get; init; }

        public string? TargetOwner { get; init; }

        public string? TargetRepository { get; init; }

        public byte[]? TargetRecordedAtUtc { get; init; }

        public byte[]? BindingId { get; init; }

        public byte[]? BindingEventId { get; init; }

        public string? RepositoryNodeId { get; init; }

        public string? BindingOwner { get; init; }

        public string? BindingRepository { get; init; }

        public byte[]? BindingObservedAtUtc { get; init; }

        public byte[]? BindingRecordedAtUtc { get; init; }

        public byte[]? ApprovedAtUtc { get; init; }

        public bool? IsHumanShape { get; init; }

        public byte[]? OccurredAtUtc { get; init; }

        public string? Payload { get; init; }
    }
}
