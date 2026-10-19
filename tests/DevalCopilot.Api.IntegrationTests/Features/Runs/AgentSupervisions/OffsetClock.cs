namespace DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;

/// <summary>A clock whose UTC reading runs ahead of the machine's by a test-controlled offset. Its timers are the system's, so the
/// observation's real five-second heartbeat loop and one-second sweep keep their real cadence while the test jumps the logical time past
/// a deadline without waiting for it (ADR-0056).</summary>
internal sealed class OffsetClock : TimeProvider
{
    private long offsetTicks;

    public void Advance(TimeSpan by) => Interlocked.Add(ref offsetTicks, by.Ticks);

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref offsetTicks));
}
