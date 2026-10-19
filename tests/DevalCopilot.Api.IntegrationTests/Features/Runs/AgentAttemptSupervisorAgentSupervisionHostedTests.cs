using DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;
using DevalCopilot.Application.Features.Runs.Ports;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>ADR-0056 observation of the real Codex planning supervisor (ADR-0009 Planner, Proposal contract), through the shared hosted
/// scenarios of <see cref="AgentSupervisionHostedScenarios"/>.</summary>
public sealed partial class AgentAttemptSupervisorHostedTests
{
    private AgentSupervisionHostedComposition AgentSupervisionComposition() => new()
    {
        Build = (reader, configure) => BuildServiceProvider(
            reader,
            new HoldingCodexPlanningAdapter(new FakeCodexPlanningAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidFinalResponseJson }),
            configure),
        Seed = async (provider, _) =>
        {
            var (runId, attemptId, _, _) = await SeedEligibleAgentAttemptAsync(provider);
            return (runId, attemptId);
        },
        CreateSupervisor = CreateSupervisor,
        AdapterOf = provider => provider.GetRequiredService<ICodexPlanningAdapter>(),
        DatabasePath = () => _databasePath,
        EvidenceForCall = _ => GateableEvidenceReader.Matching(Fingerprint),
        OpenAdapter = adapter => ((HoldingCodexPlanningAdapter)adapter).Release(),
        Hold = new AdapterHold(adapter => ((HoldingCodexPlanningAdapter)adapter).Entered.Task, adapter => ((HoldingCodexPlanningAdapter)adapter).Release()),
    };

    [Fact]
    public Task Agent_supervision_spans_the_planner_through_its_terminal_result() =>
        AgentSupervisionHostedScenarios.ObservedThroughToItsTerminalResultAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_marks_a_blocked_planner_preparation_while_the_original_path_completes() =>
        AgentSupervisionHostedScenarios.BlockedPreparationIsMarkedWhileTheOriginalPathStillCompletesAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_marks_a_blocked_planner_adapter_at_its_timeout_plus_grace() =>
        AgentSupervisionHostedScenarios.ABlockedAdapterIsMarkedAtItsTimeoutPlusGraceAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_marks_a_blocked_planner_recorder_while_the_known_result_is_still_recorded() =>
        AgentSupervisionHostedScenarios.BlockedRecorderIsMarkedWhileTheKnownResultIsStillRecordedAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_faults_never_change_the_planner_result() =>
        AgentSupervisionHostedScenarios.ObservationFaultsNeverChangeTheOriginalResultAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_closes_on_shutdown_during_AgentAttempt_preparation_and_leaves_the_attempt_to_reconciliation() =>
        AgentSupervisionHostedScenarios.ShutdownDuringPreparationClosesTheObservationAndLeavesTheAttemptToReconciliationAsync(AgentSupervisionComposition());

    [Fact]
    public Task An_unavailable_agent_supervision_store_never_blocks_the_planner() =>
        AgentSupervisionHostedScenarios.AnUnavailableObservationStoreNeverBlocksTheOriginalPathAsync(AgentSupervisionComposition());

    /// <summary>Holds the adapter call until released, counting the entry like the real invocation boundary.</summary>
    private sealed class HoldingCodexPlanningAdapter(FakeCodexPlanningAdapter inner) : ICodexPlanningAdapter
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int entries;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int InvocationCount => Volatile.Read(ref entries);

        public void Release() => release.TrySetResult();

        public async Task<CodexPlanningInvocationResult> InvokeAsync(CodexPlanningInvocationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref entries);
            Entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await inner.InvokeAsync(request, cancellationToken);
        }
    }
}
