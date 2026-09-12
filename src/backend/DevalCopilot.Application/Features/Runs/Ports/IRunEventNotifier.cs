namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Announces that durable state for a run advanced. This is a post-commit notification
/// only; SQLite and the read models remain authoritative, and reconnecting clients must
/// still query events after their last applied sequence.
/// </summary>
public interface IRunEventNotifier
{
    Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken);
}
