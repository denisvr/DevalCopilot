using DevalCopilot.Application.Data;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>A step probe that answers a fixed result (or throws), so a test decides what an independent read of a claim proved
/// without depending on the database.</summary>
internal sealed class ConfigurableLocalExecutionStepProbe(LocalExecutionStepProbeResult result, Exception? failure = null) : ILocalExecutionStepProbe
{
    public int Calls { get; private set; }

    public Task<LocalExecutionStepProbeResult> CheckAsync(Guid stepId, CancellationToken cancellationToken)
    {
        Calls++;
        return failure is null ? Task.FromResult(result) : Task.FromException<LocalExecutionStepProbeResult>(failure);
    }
}
