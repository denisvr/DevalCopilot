using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;

/// <summary>The one write boundary of an Agent supervision observation (ADR-0056). Every writer first takes the database write lock by
/// touching only the exact open receipt it may write, then reads the fresh row inside that same transaction, so a late writer judges the
/// deadline and freshness the previous writer left, never a stale read. The first attention is recorded before any heartbeat or phase
/// is, so a late writer cannot erase a gap merely because it beat the independent sweep.</summary>
internal static class AgentSupervisionWrites
{
    /// <summary>Locks the exact open receipt owned by this host and observer. False when there is none, nothing was locked or changed.</summary>
    public static async Task<bool> LockOpenOwnedAsync(
        IDevalCopilotDbContext dbContext, Guid attemptId, Guid hostInstanceId, Guid observerId, CancellationToken cancellationToken) =>
        await dbContext.AgentSupervisions
            .Where(candidate => candidate.AttemptId == attemptId
                && candidate.HostInstanceId == hostInstanceId
                && candidate.ObserverId == observerId
                && candidate.ClosedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Revision, candidate => candidate.Revision), cancellationToken) == 1;

    /// <summary>Locks one open receipt by identity, whatever host and observer wrote it (the sweep and startup closure).</summary>
    public static async Task<bool> LockOpenAsync(IDevalCopilotDbContext dbContext, Guid supervisionId, CancellationToken cancellationToken) =>
        await dbContext.AgentSupervisions
            .Where(candidate => candidate.Id == supervisionId && candidate.ClosedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Revision, candidate => candidate.Revision), cancellationToken) == 1;

    /// <summary>Why an owned write found no open receipt: absent, written by another identity, or already closed.</summary>
    public static async Task<Error> RefusalAsync(
        IDevalCopilotDbContext dbContext, Guid attemptId, Guid hostInstanceId, Guid observerId, CancellationToken cancellationToken)
    {
        var known = await dbContext.AgentSupervisions.AsNoTracking()
            .Where(candidate => candidate.AttemptId == attemptId)
            .Select(candidate => new { candidate.HostInstanceId, candidate.ObserverId, Closed = candidate.ClosedAtUtc != null })
            .SingleOrDefaultAsync(cancellationToken);
        if (known is null)
        {
            return AgentSupervisionErrors.ObservationNotFound();
        }

        if (known.HostInstanceId != hostInstanceId || known.ObserverId != observerId)
        {
            return AgentSupervisionErrors.NotOwner();
        }

        return known.Closed ? AgentSupervisionErrors.Closed() : AgentSupervisionErrors.PersistenceFailed();
    }

    /// <summary>Records the effective attention as the receipt's first attention when it is not durable yet, with its event.</summary>
    public static void RecordFirstAttention(
        AgentSupervision receipt, AgentSupervisionAssessment assessment, DateTimeOffset nowUtc, List<RunEvent> events)
    {
        if (assessment is { ChronologyValid: true, EffectiveReason: { } reason, Recorded: false }
            && receipt.RecordAttention(reason, assessment.EffectiveAtUtc ?? nowUtc))
        {
            events.Add(AgentSupervisionEvents.Attention(receipt, nowUtc));
        }
    }

    /// <summary>Records the attention of an observer that ended (or a host that was interrupted) without a durable terminal result.</summary>
    public static void RecordAttentionIfNone(
        AgentSupervision receipt, string reasonCode, DateTimeOffset nowUtc, List<RunEvent> events)
    {
        if (receipt.RecordAttention(reasonCode, nowUtc))
        {
            events.Add(AgentSupervisionEvents.Attention(receipt, nowUtc));
        }
    }

    /// <summary>Run-advance notifications are only hints (ADR-0003): a notifier failure after the commit never changes the recorded
    /// transition. Cancellation still propagates.</summary>
    public static async Task NotifyAsync(
        IRunEventNotifier? notifier, Guid runId, long sequence, CancellationToken cancellationToken)
    {
        if (notifier is null)
        {
            return;
        }

        try
        {
            await notifier.NotifyRunAdvancedAsync(runId, sequence, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The event is durable; a later cursor read delivers it.
        }
    }
}
