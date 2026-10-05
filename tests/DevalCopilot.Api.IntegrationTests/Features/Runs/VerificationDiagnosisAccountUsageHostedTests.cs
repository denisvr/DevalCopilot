using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using DevalCopilot.Application.Features.Runs.Commands.SetCodexAccountUsageStop;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>The run-scoped Codex account-usage stop (ADR-0025) through the real verification-diagnosis supervisor (see the planning class
/// in <c>AgentAttemptSupervisorAccountUsageHostedTests</c>), over the same real implementation, verification and claim pipeline the
/// other diagnosis hosted tests use.</summary>
public sealed partial class VerificationDiagnosisHostedTests
{
    private (Host Host, AccountUsageHostedHarness Harness) AccountUsageDiagnosisHost()
    {
        var scripted = new ScriptedAccountUsageAdapter();
        var host = BuildHost(accountUsage: scripted);
        var harness = new AccountUsageHostedHarness(
            scripted,
            async () =>
            {
                var lineage = await SeedLineageAsync(host, revised: false);
                var (_, report) = await ImplementAsync(host, lineage);
                await VerifyAsync(host, lineage, exitCode: 1);
                Assert.True((await SendAsync(host, new SetCodexAccountUsageStopCommand(lineage.RunId, AccountUsageHostedScenarios.Threshold))).IsSuccess);
                var claim = await SendAsync(host, new CreateVerificationDiagnosisAttemptCommand(lineage.RunId, report.Id));
                Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
                host.Diagnosis.FinalResponseJson = FindingsJson(1);
                return (lineage.RunId, claim.Value.AttemptId);
            },
            () => DiagnosisSupervisorFor(host),
            attemptId => AccountUsageHostedScenarios.ReadViewAsync(host.Provider, attemptId),
            () => host.Diagnosis.InvocationCount,
            sql => AccountUsageHostedScenarios.ExecuteSqlAsync(host.Provider, sql));
        return (host, harness);
    }

    [Fact]
    public async Task A_stop_reached_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider()
    {
        var (host, harness) = AccountUsageDiagnosisHost();
        await using var _ = host;
        await AccountUsageHostedScenarios.A_stop_reached_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider(harness, unavailable: false);
    }

    [Fact]
    public async Task An_account_that_cannot_be_observed_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider()
    {
        var (host, harness) = AccountUsageDiagnosisHost();
        await using var _ = host;
        await AccountUsageHostedScenarios.A_stop_reached_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider(harness, unavailable: true);
    }

    [Fact]
    public async Task A_percentage_equal_to_the_threshold_at_dispatch_stops_the_attempt()
    {
        var (host, harness) = AccountUsageDiagnosisHost();
        await using var _ = host;
        await AccountUsageHostedScenarios.A_percentage_equal_to_the_threshold_at_dispatch_stops_the_attempt(harness);
    }

    [Fact]
    public async Task A_healthy_account_dispatches_and_the_provider_runs_once()
    {
        var (host, harness) = AccountUsageDiagnosisHost();
        await using var _ = host;
        await AccountUsageHostedScenarios.A_healthy_account_dispatches_and_the_provider_runs_once(harness);
    }

    [Fact]
    public async Task A_refusal_that_cannot_be_recorded_fails_closed_and_later_below_threshold_observations_never_dispatch_it()
    {
        var (host, harness) = AccountUsageDiagnosisHost();
        await using var _ = host;
        await AccountUsageHostedScenarios.A_refusal_that_cannot_be_recorded_fails_closed_and_later_below_threshold_observations_never_dispatch_it(harness);
    }
}
