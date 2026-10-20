using DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>ADR-0056 observation of the real Claude review-correction supervisor (ReviewCorrection contract, a mutating role), through
/// the shared hosted scenarios of <see cref="AgentSupervisionHostedScenarios"/>, including a held adapter call whose
/// AwaitingAdapterResult deadline is the attempt's reserved invocation timeout plus two minutes. The observation never admits a writer.</summary>
public sealed partial class ReviewCorrectionSupervisorHostedTests
{
    private AgentSupervisionHostedComposition AgentSupervisionComposition() => new()
    {
        Build = (reader, configure) => BuildProvider(reader, new GatedAdapter(_artifactStore), new TestNotifier(), configure),
        Seed = async (provider, _) =>
        {
            var seed = await SeedAsync(provider);
            ((GatedAdapter)provider.GetRequiredService<IClaudeReviewCorrectionAdapter>()).FindingId = seed.FindingId;
            return (seed.RunId, seed.CorrectionId);
        },
        CreateSupervisor = provider => CreateSupervisor(
            provider,
            (GatedAdapter)provider.GetRequiredService<IClaudeReviewCorrectionAdapter>(),
            provider.GetRequiredService<IGitWorkspaceEvidenceReader>()),
        AdapterOf = provider => provider.GetRequiredService<IClaudeReviewCorrectionAdapter>(),
        DatabasePath = () => _databasePath,
        EvidenceForCall = call => call == 1 ? Matching() : Changed(),
        OpenAdapter = adapter => ((GatedAdapter)adapter).Release(),
        Hold = new AdapterHold(adapter => ((GatedAdapter)adapter).Invoked.Task, adapter => ((GatedAdapter)adapter).Release()),
    };

    [Fact]
    public Task A_claimed_review_correction_attempt_waits_for_an_existing_workspace_owner_without_being_terminalized_and_then_runs_once() =>
        WorkspaceExecutionHostedScenarios.AClaimedAttemptWaitsForAnExistingOwnerAndThenRunsOnceAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_spans_the_corrector_through_its_terminal_result() =>
        AgentSupervisionHostedScenarios.ObservedThroughToItsTerminalResultAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_marks_a_blocked_corrector_preparation_while_the_original_path_completes() =>
        AgentSupervisionHostedScenarios.BlockedPreparationIsMarkedWhileTheOriginalPathStillCompletesAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_marks_a_blocked_corrector_adapter_at_its_timeout_plus_grace() =>
        AgentSupervisionHostedScenarios.ABlockedAdapterIsMarkedAtItsTimeoutPlusGraceAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_marks_a_blocked_corrector_recorder_while_the_known_result_is_still_recorded() =>
        AgentSupervisionHostedScenarios.BlockedRecorderIsMarkedWhileTheKnownResultIsStillRecordedAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_faults_never_change_the_corrector_result() =>
        AgentSupervisionHostedScenarios.ObservationFaultsNeverChangeTheOriginalResultAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_closes_on_shutdown_during_ReviewCorrection_preparation_and_leaves_the_attempt_to_reconciliation() =>
        AgentSupervisionHostedScenarios.ShutdownDuringPreparationClosesTheObservationAndLeavesTheAttemptToReconciliationAsync(AgentSupervisionComposition());

    [Fact]
    public Task An_unavailable_agent_supervision_store_never_blocks_the_corrector() =>
        AgentSupervisionHostedScenarios.AnUnavailableObservationStoreNeverBlocksTheOriginalPathAsync(AgentSupervisionComposition());
}
