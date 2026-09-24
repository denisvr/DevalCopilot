using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Domain.Tests;

/// <summary>Deterministic host-measured process evidence for tests that record an outcome requiring
/// a clean provider exit. Never produced by a real provider process.</summary>
internal static class TestProcessEvidence
{
    public static AgentProcessExecutionEvidence CleanExit { get; } =
        AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.FromMilliseconds(1250));
}
