using DevalCopilot.Api.HostedServices;
using DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>ADR-0056 observation of the real Codex verification-diagnosis supervisor (VerificationDiagnosis contract), through the
/// shared hosted scenarios of <see cref="AgentSupervisionHostedScenarios"/>. The diagnosis attempt is reached through the class's own
/// production-written lineage (implementation, a failed verification and the explicit diagnosis request); only the supervisor under
/// test captures Git evidence through the holdable reader.</summary>
public sealed partial class VerificationDiagnosisHostedTests
{
    private AgentSupervisionHostedComposition AgentSupervisionComposition()
    {
        Host? host = null;
        StagedEvidence staged = new();
        GateableEvidenceReader? gate = null;

        return new AgentSupervisionHostedComposition
        {
            Build = (reader, configure) =>
            {
                gate = reader;
                staged = new StagedEvidence();
                host = BuildHost(staged, accountUsage: null, configure);
                return host.Provider;
            },
            Seed = async (_, _) =>
            {
                var lineage = await SeedLineageAsync(host!, revised: false);
                var (_, report) = await ImplementAsync(host!, lineage);
                await VerifyAsync(host!, lineage, exitCode: 1);
                var claim = await SendAsync(host!, new CreateVerificationDiagnosisAttemptCommand(lineage.RunId, report.Id));
                Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
                host!.Diagnosis.FinalResponseJson = FindingsJson(2);
                return (lineage.RunId, claim.Value.AttemptId);
            },
            CreateSupervisor = provider => new VerificationDiagnosisSupervisor(
                provider.GetRequiredService<IServiceScopeFactory>(),
                host!.Diagnosis,
                gate!,
                provider.GetRequiredService<Application.Features.Processes.Ports.IArtifactStore>(),
                NullLogger<VerificationDiagnosisSupervisor>.Instance),
            AdapterOf = _ => host!.Diagnosis,
            DatabasePath = () => _databasePath,
            EvidenceForCall = _ => staged.CaptureAsync(string.Empty, CancellationToken.None).GetAwaiter().GetResult(),
        };
    }

    [Fact]
    public Task Agent_supervision_spans_the_diagnosis_through_its_terminal_result() =>
        AgentSupervisionHostedScenarios.ObservedThroughToItsTerminalResultAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_marks_a_blocked_diagnosis_preparation_while_the_original_path_completes() =>
        AgentSupervisionHostedScenarios.BlockedPreparationIsMarkedWhileTheOriginalPathStillCompletesAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_marks_a_blocked_diagnosis_recorder_while_the_known_result_is_still_recorded() =>
        AgentSupervisionHostedScenarios.BlockedRecorderIsMarkedWhileTheKnownResultIsStillRecordedAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_faults_never_change_the_diagnosis_result() =>
        AgentSupervisionHostedScenarios.ObservationFaultsNeverChangeTheOriginalResultAsync(AgentSupervisionComposition());

    [Fact]
    public Task Agent_supervision_closes_on_shutdown_during_VerificationDiagnosis_preparation_and_leaves_the_attempt_to_reconciliation() =>
        AgentSupervisionHostedScenarios.ShutdownDuringPreparationClosesTheObservationAndLeavesTheAttemptToReconciliationAsync(AgentSupervisionComposition());

    [Fact]
    public Task An_unavailable_agent_supervision_store_never_blocks_the_diagnosis() =>
        AgentSupervisionHostedScenarios.AnUnavailableObservationStoreNeverBlocksTheOriginalPathAsync(AgentSupervisionComposition());
}
