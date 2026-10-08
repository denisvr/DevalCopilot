using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalCommit;

/// <summary>
/// Run-advance notifications are only hints: the durable, sequenced event journal is the source of truth and replays by cursor.
/// A notifier that fails after the transaction committed must therefore never turn a recorded fact into a reported failure or hide
/// a recorded outcome from the caller (ADR-0003, ADR-0029). Cancellation still propagates.
/// </summary>
internal static class LocalCommitNotification
{
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
            // The event is already durable; a later cursor read delivers it.
        }
    }
}
