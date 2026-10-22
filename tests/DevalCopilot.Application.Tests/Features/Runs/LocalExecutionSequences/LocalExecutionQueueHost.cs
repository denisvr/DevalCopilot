using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// A deterministic stand-in for the hosted coordinator's queue duties (ADR-0062), built only from the production commands the host
/// sends: one bounded observation sweep of the waiting queue, the per-sequence observation of every claimed or preparing cycle (which
/// concludes steps and records successors), the oldest-eligible reservations (at most four per tick, each worker acquired first), the
/// claim of each ticket exactly once and the scripted dispatch and result of the claimed operation. It selects no input of its own, never
/// retries a claim and never resumes anything: whatever the database refuses stays refused.
/// </summary>
internal sealed class LocalExecutionQueueHost(
    params (LocalExecutionSequenceTestScene Scene, LocalExecutionStepScript Script)[] participants)
{
    /// <summary>One tick; returns how many tickets were reserved and driven.</summary>
    public async Task<int> TickAsync(bool workerAvailable = true, int maximumReservations = 4)
    {
        var first = participants[0].Scene;
        await first.ObserveWholeQueueAsync(workerAvailable);
        foreach (var (scene, _) in participants)
        {
            var sequence = await scene.FindSequenceAsync();
            if (sequence is not null && sequence.State is LocalExecutionSequenceState.PreparingStep or LocalExecutionSequenceState.AwaitingStepResult or LocalExecutionSequenceState.Stopping)
            {
                await scene.ObserveAsync(sequence.Id);
            }
        }

        var driven = 0;
        for (var index = 0; index < maximumReservations; index++)
        {
            var ticket = await first.ReserveAsync();
            if (ticket is null)
            {
                break;
            }

            var participant = await ParticipantOfAsync(ticket.SequenceId);
            await participant.Scene.DriveTicketAsync(ticket, participant.Script);
            driven++;
        }

        foreach (var (scene, _) in participants)
        {
            scene.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        return driven;
    }

    /// <summary>Ticks until <paramref name="done"/> holds, at most <paramref name="maximumTicks"/>; fails when it never does.</summary>
    public async Task RunUntilAsync(Func<Task<bool>> done, int maximumTicks = 400)
    {
        for (var tick = 0; tick < maximumTicks; tick++)
        {
            if (await done())
            {
                return;
            }

            await TickAsync();
        }

        Assert.True(await done(), "The queue host never reached the awaited condition.");
    }

    /// <summary>Ticks until every participant's cycle has ended.</summary>
    public Task RunUntilAllEndedAsync(int maximumTicks = 400) => RunUntilAsync(
        async () =>
        {
            foreach (var (scene, _) in participants)
            {
                var sequence = await scene.FindSequenceAsync();
                if (sequence is null || LocalExecutionSequencePolicy.IsAuthorityLive(sequence.State))
                {
                    return false;
                }
            }

            return true;
        },
        maximumTicks);

    private async Task<(LocalExecutionSequenceTestScene Scene, LocalExecutionStepScript Script)> ParticipantOfAsync(Guid sequenceId)
    {
        foreach (var participant in participants)
        {
            if ((await participant.Scene.FindSequenceAsync())?.Id == sequenceId)
            {
                return participant;
            }
        }

        throw new InvalidOperationException("The reserved step belongs to no participant of this host.");
    }
}
