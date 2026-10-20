using DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;
using DevalCopilot.Application.Features.Runs.Ports;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>ADR-0056 observation of the real Codex challenge-resolution supervisor (ChallengeResolution contract), through the shared
/// hosted scenarios of <see cref="AgentSupervisionHostedScenarios"/>.</summary>
public sealed partial class ChallengeResolutionSupervisorHostedTests
{
    private AgentSupervisionHostedComposition AgentSupervisionComposition() => new()
    {
        Build = (reader, configure) => BuildServiceProvider(
            reader, new FakeChallengeResolutionAdapter(_artifactStore), accountUsage: null, configureAdditionalServices: configure),
        Seed = async (provider, reader) =>
        {
            var (runId, attemptId, _, _, challengeIds) = await SeedEligibleChallengeResolutionAttemptAsync(provider, reader);
            ((FakeChallengeResolutionAdapter)provider.GetRequiredService<ICodexChallengeResolutionAdapter>()).FinalResponseJsonToWrite =
                ResolvedFinalResponseJson(challengeIds);
            return (runId, attemptId);
        },
        CreateSupervisor = CreateSupervisor,
        AdapterOf = provider => provider.GetRequiredService<ICodexChallengeResolutionAdapter>(),
        DatabasePath = () => _databasePath,
        EvidenceForCall = _ => GateableEvidenceReader.Matching(Fingerprint),
    };

    [Fact]
    public Task A_claimed_resolver_attempt_waits_for_an_existing_workspace_owner_without_being_terminalized_and_then_runs_once() =>
        WorkspaceExecutionHostedScenarios.AClaimedAttemptWaitsForAnExistingOwnerAndThenRunsOnceAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_spans_the_resolver_through_its_terminal_result() =>
        AgentSupervisionHostedScenarios.ObservedThroughToItsTerminalResultAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_marks_a_blocked_resolver_preparation_while_the_original_path_completes() =>
        AgentSupervisionHostedScenarios.BlockedPreparationIsMarkedWhileTheOriginalPathStillCompletesAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_marks_a_blocked_resolver_recorder_while_the_known_result_is_still_recorded() =>
        AgentSupervisionHostedScenarios.BlockedRecorderIsMarkedWhileTheKnownResultIsStillRecordedAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_faults_never_change_the_resolver_result() =>
        AgentSupervisionHostedScenarios.ObservationFaultsNeverChangeTheOriginalResultAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_closes_on_shutdown_during_ChallengeResolution_preparation_and_leaves_the_attempt_to_reconciliation() =>
        AgentSupervisionHostedScenarios.ShutdownDuringPreparationClosesTheObservationAndLeavesTheAttemptToReconciliationAsync(AgentSupervisionComposition());

    [Fact]
    public Task An_unavailable_agent_supervision_store_never_blocks_the_resolver() =>
        AgentSupervisionHostedScenarios.AnUnavailableObservationStoreNeverBlocksTheOriginalPathAsync(AgentSupervisionComposition());
}
