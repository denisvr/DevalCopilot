using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;

/// <summary>
/// The bounded, untracked scalar projection of the recorded abandonment inspection (ADR-0071). Nothing outside the listed columns is
/// ever selected: no objective, stage, budget, attempt, message, verification, workspace, lease, commit or artifact fact, and no other
/// event of the run. The stored mode and lifecycle are classified by the database, every identifier, the last-advance time, the reason
/// and the event payload and time are projected as raw stored bytes within their bound (a cast to a blob counts an embedded NUL), so a
/// value beyond its bound is never fetched and malformed storage reads as an explicit refusal rather than a plausible value or a
/// conversion fault. The recorded abandonment time alone goes through its own TEXT-only tolerant column mapping, bounded the same way.
/// A <c>Present</c> flag tells a rejected value from an absent one.
/// </summary>
internal static class RecordedAbandonmentProjection
{
    private const int ModeByteBound = 8;
    private const int IdentifierBytes = RecordedAbandonmentStorage.IdentifierBytes;
    private const int TimeBytes = RecordedAbandonmentStorage.TimeBytes;
    private const int ReasonBytes = RecordedAbandonmentStorage.ReasonBytes;
    private const int PayloadBytes = RecordedAbandonmentStorage.PayloadBytes;

    public static IQueryable<RecordedAbandonmentRow> Project(this IQueryable<RecordedAbandonmentJoin> joins) =>
        joins.Select(row => new RecordedAbandonmentRow(
            ((byte[])(object)row.Run.Id).Length <= IdentifierBytes ? (byte[])(object)row.Run.Id : null,
            ((byte[])(object)row.Run.ProjectId).Length <= IdentifierBytes ? (byte[])(object)row.Run.ProjectId : null,
            ((byte[])(object)row.Project.Id).Length <= IdentifierBytes ? (byte[])(object)row.Project.Id : null,
            ((byte[])(object)EF.Property<string>(row.Run, Run.ExecutionModeStorageProperty)).Length <= ModeByteBound
                ? EF.Property<string>(row.Run, Run.ExecutionModeStorageProperty)
                : null,
            row.Run.Lifecycle == RunLifecycle.Created ? 1
                : row.Run.Lifecycle == RunLifecycle.Running ? 2
                : row.Run.Lifecycle == RunLifecycle.Completed ? 3
                : row.Run.Lifecycle == RunLifecycle.Failed ? 4
                : row.Run.Lifecycle == RunLifecycle.Interrupted ? 5
                : row.Run.Lifecycle == RunLifecycle.Abandoned ? 6
                : 0,
            row.Run.ActiveParticipantKind == ParticipantKind.None
                && row.Run.ActiveAgentRole == null
                && row.Run.ActiveAgentProvider == null,
            ((byte[])(object)row.Run.LastAdvancedAtUtc).Length <= TimeBytes ? (byte[])(object)row.Run.LastAdvancedAtUtc : null,
            row.Run.AbandonmentReason != null,
            ((byte[])(object)row.Run.AbandonmentReason!).Length <= ReasonBytes ? (byte[])(object)row.Run.AbandonmentReason : null,
            row.Run.AbandonedAtUtc != null,
            ((byte[])(object)row.Run.AbandonedAtUtc!).Length <= TimeBytes ? row.Run.AbandonedAtUtc : null,
            (byte[]?)(object)row.AbandonedEvent.Id != null,
            ((byte[])(object)row.AbandonedEvent.Id).Length <= IdentifierBytes ? (byte[])(object)row.AbandonedEvent.Id : null,
            row.AbandonedEvent.Sequence >= 1 ? row.AbandonedEvent.Sequence : null,
            ((byte[])(object)row.AbandonedEvent.RunId).Length <= IdentifierBytes ? (byte[])(object)row.AbandonedEvent.RunId : null,
            row.AbandonedEvent.ActorKind == ParticipantKind.Human
                && row.AbandonedEvent.ActorAgentRole == null
                && row.AbandonedEvent.ActorAgentProvider == null
                && row.AbandonedEvent.AttemptId == null,
            ((byte[])(object)row.AbandonedEvent.PayloadJson).Length <= PayloadBytes ? (byte[])(object)row.AbandonedEvent.PayloadJson : null,
            ((byte[])(object)row.AbandonedEvent.OccurredAtUtc).Length <= TimeBytes
                ? (byte[])(object)row.AbandonedEvent.OccurredAtUtc
                : null));
}
