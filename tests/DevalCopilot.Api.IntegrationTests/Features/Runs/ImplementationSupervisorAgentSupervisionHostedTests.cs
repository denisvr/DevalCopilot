using DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>ADR-0056 observation of the real Claude implementation supervisor (ImplementationReport contract, a mutating role), through
/// the shared hosted scenarios of <see cref="AgentSupervisionHostedScenarios"/>. The observation never admits a writer or alters the
/// attempt; the adapter and its source evidence are the class's own doubles.</summary>
public sealed partial class ImplementationSupervisorHostedTests
{
    private AgentSupervisionHostedComposition AgentSupervisionComposition() => new()
    {
        Build = (reader, configure) => BuildServiceProvider(
            reader,
            new FakeClaudeImplementationAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidImplementationReportJson(["src/Foo.cs"]) },
            configure),
        Seed = async (provider, reader) =>
        {
            var (runId, attemptId, _, _) = await SeedEligibleImplementationAttemptAsync(provider, reader);
            return (runId, attemptId);
        },
        CreateSupervisor = CreateSupervisor,
        AdapterOf = provider => provider.GetRequiredService<IClaudeImplementationAdapter>(),
        DatabasePath = () => _databasePath,
        EvidenceForCall = call => call <= 4
            ? GateableEvidenceReader.Matching(Fingerprint)
            : MatchingWithChangedPaths(ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")]),
    };

    [Fact]
    public Task Agent_supervision_spans_the_implementer_through_its_terminal_result() =>
        AgentSupervisionHostedScenarios.ObservedThroughToItsTerminalResultAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_marks_a_blocked_implementer_preparation_while_the_original_path_completes() =>
        AgentSupervisionHostedScenarios.BlockedPreparationIsMarkedWhileTheOriginalPathStillCompletesAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_marks_a_blocked_implementer_recorder_while_the_known_result_is_still_recorded() =>
        AgentSupervisionHostedScenarios.BlockedRecorderIsMarkedWhileTheKnownResultIsStillRecordedAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_faults_never_change_the_implementer_result() =>
        AgentSupervisionHostedScenarios.ObservationFaultsNeverChangeTheOriginalResultAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_closes_on_shutdown_during_Implementation_preparation_and_leaves_the_attempt_to_reconciliation() =>
        AgentSupervisionHostedScenarios.ShutdownDuringPreparationClosesTheObservationAndLeavesTheAttemptToReconciliationAsync(AgentSupervisionComposition());

    [Fact]
    public Task An_unavailable_agent_supervision_store_never_blocks_the_implementer() =>
        AgentSupervisionHostedScenarios.AnUnavailableObservationStoreNeverBlocksTheOriginalPathAsync(AgentSupervisionComposition());
}
