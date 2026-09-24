using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.IntegrationTests;

/// <summary>Deterministic host-measured process evidence for tests that record an outcome requiring
/// a clean provider exit. Never produced by a real provider process.</summary>
internal static class TestProcessEvidence
{
    /// <summary>Domain evidence passed directly to an Agent completion transition.</summary>
    public static AgentProcessExecutionEvidence CleanExit { get; } =
        AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.FromMilliseconds(1250));

    /// <summary>The same evidence as an adapter reports it through the provider-neutral port.</summary>
    public static AgentProcessEvidence ReportedCleanExit { get; } =
        new(ProcessExecutionOutcome.Exited, 0, TimeSpan.FromMilliseconds(1250));
}
