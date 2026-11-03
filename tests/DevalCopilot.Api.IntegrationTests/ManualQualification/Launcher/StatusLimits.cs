namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>Lot A's bounds: each child's execution deadline (the process adapter adds its own unchanged 5 s post-kill, 5 s I/O and 1 s
/// release windows), the serial sequence's bound that cancels a running child, and the outer bound of the whole entry including
/// closure and the report. A sequence still running at the outer bound is reported as unproven and left untouched.</summary>
public sealed record StatusLimits(TimeSpan Execution, TimeSpan Sequence, TimeSpan Outer)
{
    public static StatusLimits Production { get; } = new(
        Execution: TimeSpan.FromSeconds(30),
        Sequence: TimeSpan.FromSeconds(100),
        Outer: TimeSpan.FromMinutes(2));
}
