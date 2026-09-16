using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetProviderRuntimePreflight;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.EnvironmentReadiness;

public sealed class ProviderRuntimePreflightProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_successful_version_probe_is_available_but_keeps_unobserved_provider_capabilities_explicit()
    {
        var codex = CreateSnapshot(Capability.CodexCli, CapabilityProbeReason.None, Now.AddMinutes(5));

        var result = Project([codex]);

        var runtime = Assert.Single(result, candidate => candidate.Provider == ProviderRuntime.Codex);
        Assert.Equal(ProviderRuntimeStatus.Available, runtime.Status);
        Assert.Equal("1.2.3", runtime.ObservedVersion);
        Assert.Equal(ProviderRuntimeEvidenceFreshness.Fresh, runtime.EvidenceFreshness);
        Assert.Equal("provider_runtime.version_observed", runtime.ReasonCode);
        Assert.Equal(ProviderRuntimeCapabilityStatus.Unknown, runtime.Authentication);
        Assert.Equal(ProviderRuntimeCapabilityStatus.Unknown, runtime.ModelCatalog);
        Assert.Equal(ProviderRuntimeCapabilityStatus.Unknown, runtime.ReasoningEffort);
        Assert.Equal(ProviderRuntimeCapabilityStatus.Unknown, runtime.PermissionMode);
        Assert.Equal(ProviderRuntimeCapabilityStatus.Unknown, runtime.ContextUsage);
        Assert.Equal(ProviderRuntimeCapabilityStatus.Unknown, runtime.Compaction);
        Assert.Equal(ProviderRuntimeCapabilityStatus.Unknown, runtime.Sessions);
        Assert.Equal(ProviderRuntimeCapabilityStatus.Unknown, runtime.AccountUsage);
    }

    [Fact]
    public void A_missing_runtime_is_unavailable_without_any_provider_capability_claim()
    {
        var claude = CreateSnapshot(Capability.ClaudeCli, CapabilityProbeReason.ExecutableNotFound, Now.AddMinutes(5));

        var runtime = Assert.Single(Project([claude]), candidate => candidate.Provider == ProviderRuntime.ClaudeCode);

        Assert.Equal(ProviderRuntimeStatus.Unavailable, runtime.Status);
        Assert.Null(runtime.ObservedVersion);
        Assert.Equal("provider_runtime.executable_not_found", runtime.ReasonCode);
        Assert.Equal(ProviderRuntimeCapabilityStatus.Unknown, runtime.Authentication);
    }

    [Fact]
    public void Unsupported_is_not_a_preflight_capability_state_without_affirmative_provider_evidence()
    {
        Assert.Equal([ProviderRuntimeCapabilityStatus.Unknown], Enum.GetValues<ProviderRuntimeCapabilityStatus>());
    }

    [Fact]
    public void An_unprobed_runtime_is_checking_with_not_observed_evidence()
    {
        var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);

        var runtime = Assert.Single(Project([codex]), candidate => candidate.Provider == ProviderRuntime.Codex);

        Assert.Equal(ProviderRuntimeStatus.Checking, runtime.Status);
        Assert.Equal(ProviderRuntimeEvidenceFreshness.NotObserved, runtime.EvidenceFreshness);
        Assert.Equal("provider_runtime.not_observed", runtime.ReasonCode);
    }

    [Theory]
    [InlineData(CapabilityProbeReason.ExecutableInaccessible)]
    [InlineData(CapabilityProbeReason.ProbeTimedOut)]
    [InlineData(CapabilityProbeReason.ProbeInterruptedByRestart)]
    public void Attention_reasons_reuse_the_generic_readiness_mapping(CapabilityProbeReason reason)
    {
        var codex = CreateSnapshot(Capability.CodexCli, reason, Now.AddMinutes(5));

        var runtime = Assert.Single(Project([codex]), candidate => candidate.Provider == ProviderRuntime.Codex);

        Assert.Equal(ProviderRuntimeStatus.NeedsAttention, runtime.Status);
    }

    [Fact]
    public void Stale_version_evidence_remains_available_but_is_marked_stale()
    {
        var codex = CreateSnapshot(Capability.CodexCli, CapabilityProbeReason.None, Now);

        var runtime = Assert.Single(Project([codex], Now.AddHours(1)), candidate => candidate.Provider == ProviderRuntime.Codex);

        Assert.Equal(ProviderRuntimeStatus.Available, runtime.Status);
        Assert.Equal(ProviderRuntimeEvidenceFreshness.Stale, runtime.EvidenceFreshness);
    }

    private static IReadOnlyList<ProviderRuntimePreflightQueryResult> Project(
        IReadOnlyList<HostCapabilitySnapshot> snapshots, DateTimeOffset? atUtc = null) =>
        ProviderRuntimePreflightProjector.Project(HostCapabilityReadinessProjector.Project(snapshots, atUtc ?? Now));

    private static HostCapabilitySnapshot CreateSnapshot(
        Capability capability, CapabilityProbeReason reason, DateTimeOffset nextProbeDueAtUtc)
    {
        var snapshot = HostCapabilitySnapshot.Seed(capability, Now);
        if (reason == CapabilityProbeReason.NeverProbed)
        {
            return snapshot;
        }

        snapshot.MarkDispatched(Now);
        if (reason == CapabilityProbeReason.None)
        {
            snapshot.RecordSuccess(@"C:\provider\runtime.exe", "1.2.3", Now, nextProbeDueAtUtc);
        }
        else if (reason == CapabilityProbeReason.ProbeInterruptedByRestart)
        {
            snapshot.ClearStuckDispatch(nextProbeDueAtUtc);
        }
        else
        {
            snapshot.RecordFailure(reason, nextProbeDueAtUtc);
        }

        return snapshot;
    }
}
