using DevalCopilot.Application.Data;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>An independent durability probe that reports a fixed answer, to force the ambiguous-outcome branches
/// (<see cref="AttemptDurabilityCheckResult.Unresolved"/>) that a real probe over a healthy database never returns.</summary>
internal sealed class FixedDurabilityProbe(AttemptDurabilityCheckResult result) : IAttemptDurabilityProbe
{
    public int Calls { get; private set; }

    public Task<AttemptDurabilityCheckResult> CheckAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(result);
    }
}
