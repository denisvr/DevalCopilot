namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one durable observation of one claimed Agent attempt by the host that is supervising it (ADR-0056). It records which host observer
/// last reported which phase, when, and the absolute deadline of that phase; it is correlation and disclosure evidence only. It never
/// claims, dispatches, retries, cancels or terminalizes an Attempt, alters a Run or workspace, releases ownership or admits a writer,
/// and neither a heartbeat nor an absent attention proves provider progress or a live child process. The identity (run, attempt, host
/// instance and observer) is immutable: only the original observer of the original live host may advance an open observation, and a
/// competing identity never replaces or adopts it. <see cref="Revision"/> is the concurrency token every transition advances, so two
/// writers can never both apply a decision made from the same read. The first attention is retained forever.
/// </summary>
public sealed class AgentSupervision
{
    private AgentSupervision()
    {
    }

    public static AgentSupervision Register(
        Guid id, Guid runId, Guid attemptId, Guid hostInstanceId, Guid observerId, DateTimeOffset startedAtUtc)
    {
        if (id == Guid.Empty || runId == Guid.Empty || attemptId == Guid.Empty || hostInstanceId == Guid.Empty || observerId == Guid.Empty)
        {
            throw new ArgumentException("An Agent supervision requires durable identifiers.");
        }

        if (startedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(startedAtUtc));
        }

        var deadline = AgentSupervisionPolicy.PhaseDeadlineOf(AgentSupervisionPhase.Preparing, startedAtUtc, null)
            ?? throw new ArgumentOutOfRangeException(nameof(startedAtUtc), "The phase deadline is not representable.");
        if (AgentSupervisionPolicy.StaleAtOf(startedAtUtc) is null)
        {
            throw new ArgumentOutOfRangeException(nameof(startedAtUtc), "The staleness boundary is not representable.");
        }

        return new AgentSupervision
        {
            Id = id,
            RunId = runId,
            AttemptId = attemptId,
            HostInstanceId = hostInstanceId,
            ObserverId = observerId,
            Revision = 1,
            StartedAtUtc = startedAtUtc,
            LastHeartbeatAtUtc = startedAtUtc,
            Phase = AgentSupervisionPhase.Preparing,
            PhaseEnteredAtUtc = startedAtUtc,
            PhaseDeadlineUtc = deadline,
        };
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid AttemptId { get; private set; }

    /// <summary>The host launch that created this observation. A correlation fact, never a credential or lease.</summary>
    public Guid HostInstanceId { get; private set; }

    /// <summary>The fresh identifier of the one supervision invocation that registered this observation.</summary>
    public Guid ObserverId { get; private set; }

    public long Revision { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    /// <summary>The last committed report of the host observer (a heartbeat or a phase entry). Only the host's own observation loop
    /// reported it.</summary>
    public DateTimeOffset LastHeartbeatAtUtc { get; private set; }

    public AgentSupervisionPhase Phase { get; private set; }

    public DateTimeOffset PhaseEnteredAtUtc { get; private set; }

    /// <summary>The absolute deadline of <see cref="Phase"/>, fixed at phase entry. A heartbeat never moves it.</summary>
    public DateTimeOffset PhaseDeadlineUtc { get; private set; }

    /// <summary>One of <see cref="AgentSupervisionAttentionReason"/>; the first attention only, never replaced or cleared.</summary>
    public string? AttentionReason { get; private set; }

    public DateTimeOffset? AttentionAtUtc { get; private set; }

    /// <summary>One of <see cref="AgentSupervisionClosureReason"/>; null while the observation is open.</summary>
    public string? ClosureReason { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public bool IsOpen => ClosedAtUtc is null;

    public bool IsOwnedBy(Guid hostInstanceId, Guid observerId) =>
        HostInstanceId == hostInstanceId && ObserverId == observerId;

    /// <summary>An identical registration replays; any other identity is a competing one.</summary>
    public bool IsIdenticalRegistration(Guid runId, Guid attemptId, Guid hostInstanceId, Guid observerId) =>
        RunId == runId && AttemptId == attemptId && IsOwnedBy(hostInstanceId, observerId);

    /// <summary>Records one committed heartbeat. It moves neither the phase nor its deadline.</summary>
    public void RecordHeartbeat(DateTimeOffset nowUtc)
    {
        RequireOpen();
        RequireNotBeforeLastReport(nowUtc);
        LastHeartbeatAtUtc = nowUtc;
        Revision++;
    }

    /// <summary>Records the first attention. Returns false, changing nothing, when an attention is already recorded.</summary>
    public bool RecordAttention(string reasonCode, DateTimeOffset atUtc)
    {
        if (!AgentSupervisionAttentionReason.IsKnown(reasonCode))
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode), "Not a fixed Agent supervision attention reason.");
        }

        if (atUtc == default || atUtc < StartedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(atUtc));
        }

        RequireOpen();
        if (AttentionReason is not null)
        {
            return false;
        }

        AttentionReason = reasonCode;
        AttentionAtUtc = atUtc;
        Revision++;
        return true;
    }

    /// <summary>Enters a strictly later phase with the deadline fixed now. The entry is itself a report of the host observer.</summary>
    public void AdvancePhase(AgentSupervisionPhase next, DateTimeOffset nowUtc, DateTimeOffset deadlineUtc)
    {
        RequireOpen();
        RequireNotBeforeLastReport(nowUtc);
        if (!Enum.IsDefined(next) || AgentSupervisionPolicy.Rank(next) <= AgentSupervisionPolicy.Rank(Phase))
        {
            throw new InvalidOperationException($"A phase moves forward only, once; {Phase} cannot enter {next}.");
        }

        if (deadlineUtc <= nowUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(deadlineUtc), "A phase deadline lies after its entry.");
        }

        Phase = next;
        PhaseEnteredAtUtc = nowUtc;
        PhaseDeadlineUtc = deadlineUtc;
        LastHeartbeatAtUtc = nowUtc;
        Revision++;
    }

    /// <summary>Closes the observation for good. Closing retains the first attention and never changes the Attempt.</summary>
    public void Close(string reasonCode, DateTimeOffset nowUtc)
    {
        if (!AgentSupervisionClosureReason.IsKnown(reasonCode))
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode), "Not a fixed Agent supervision closure reason.");
        }

        RequireOpen();
        if (nowUtc == default || nowUtc < LastHeartbeatAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(nowUtc));
        }

        ClosureReason = reasonCode;
        ClosedAtUtc = nowUtc;
        Revision++;
    }

    private void RequireOpen()
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("The Agent supervision is closed.");
        }
    }

    private void RequireNotBeforeLastReport(DateTimeOffset nowUtc)
    {
        if (nowUtc == default || nowUtc < LastHeartbeatAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(nowUtc), "An observation never reports before its last committed report.");
        }
    }
}
