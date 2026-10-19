using DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;
using DevalCopilot.Application.Features.Runs.Ports;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>ADR-0056 observation of the real Claude critical-review supervisor (CriticalReview contract), through the shared hosted
/// scenarios of <see cref="AgentSupervisionHostedScenarios"/>.</summary>
public sealed partial class ClaudeCriticalReviewSupervisorHostedTests
{
    private AgentSupervisionHostedComposition AgentSupervisionComposition() => new()
    {
        Build = (reader, configure) => BuildServiceProvider(
            reader, new FakeCriticalReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidAcceptanceFinalResponseJson }, configure),
        Seed = async (provider, reader) =>
        {
            var (runId, attemptId, _, _, _) = await SeedEligibleClaudeCriticalReviewAttemptAsync(provider, reader);
            return (runId, attemptId);
        },
        CreateSupervisor = CreateSupervisor,
        AdapterOf = provider => provider.GetRequiredService<ICriticalReviewAdapter>(),
        DatabasePath = () => _databasePath,
        EvidenceForCall = _ => GateableEvidenceReader.Matching(Fingerprint),
    };

    [Fact]
    public Task Agent_supervision_spans_the_critical_reviewer_through_its_terminal_result() =>
        AgentSupervisionHostedScenarios.ObservedThroughToItsTerminalResultAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_marks_a_blocked_critical_review_preparation_while_the_original_path_completes() =>
        AgentSupervisionHostedScenarios.BlockedPreparationIsMarkedWhileTheOriginalPathStillCompletesAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_marks_a_blocked_critical_review_recorder_while_the_known_result_is_still_recorded() =>
        AgentSupervisionHostedScenarios.BlockedRecorderIsMarkedWhileTheKnownResultIsStillRecordedAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_faults_never_change_the_critical_review_result() =>
        AgentSupervisionHostedScenarios.ObservationFaultsNeverChangeTheOriginalResultAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_closes_on_shutdown_during_ClaudeCriticalReview_preparation_and_leaves_the_attempt_to_reconciliation() =>
        AgentSupervisionHostedScenarios.ShutdownDuringPreparationClosesTheObservationAndLeavesTheAttemptToReconciliationAsync(AgentSupervisionComposition());

    [Fact]
    public Task An_unavailable_agent_supervision_store_never_blocks_the_critical_reviewer() =>
        AgentSupervisionHostedScenarios.AnUnavailableObservationStoreNeverBlocksTheOriginalPathAsync(AgentSupervisionComposition());
}
