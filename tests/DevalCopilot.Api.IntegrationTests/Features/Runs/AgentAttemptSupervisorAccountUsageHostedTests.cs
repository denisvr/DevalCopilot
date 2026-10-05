using DevalCopilot.Application.Features.Projects.Ports;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>The run-scoped Codex account-usage stop (ADR-0025) through the real planning <see cref="DevalCopilot.Api.HostedServices.AgentAttemptSupervisor"/>,
/// its real mediator, claim handler, dispatch gate and recording command over real SQLite: below the stop at the claim and a stop (or an
/// unobservable account) at dispatch gives one terminal outcome and event with zero provider invocations and no redispatch after a restart;
/// a healthy account runs the provider once.</summary>
public sealed partial class AgentAttemptSupervisorHostedTests
{
    private (ServiceProvider Provider, AccountUsageHostedHarness Harness) AccountUsagePlanningHost()
    {
        var scripted = new ScriptedAccountUsageAdapter();
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var agent = new FakeCodexPlanningAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidFinalResponseJson, StandardOutputToWrite = "codex stdout" };
        var provider = BuildServiceProvider(evidenceReader, agent, services => AccountUsageGuardTestServices.Register(services, scripted));
        var harness = new AccountUsageHostedHarness(
            scripted,
            async () =>
            {
                var (runId, attemptId, _, _) = await SeedEligibleAgentAttemptAsync(provider, AccountUsageHostedScenarios.Threshold);
                return (runId, attemptId);
            },
            () => CreateSupervisor(provider),
            attemptId => AccountUsageHostedScenarios.ReadViewAsync(provider, attemptId),
            () => agent.InvocationCount,
            sql => AccountUsageHostedScenarios.ExecuteSqlAsync(provider, sql));
        return (provider, harness);
    }

    [Fact]
    public async Task A_stop_reached_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider()
    {
        var (provider, harness) = AccountUsagePlanningHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_stop_reached_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider(harness, unavailable: false);
    }

    [Fact]
    public async Task An_account_that_cannot_be_observed_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider()
    {
        var (provider, harness) = AccountUsagePlanningHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_stop_reached_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider(harness, unavailable: true);
    }

    [Fact]
    public async Task A_percentage_equal_to_the_threshold_at_dispatch_stops_the_attempt()
    {
        var (provider, harness) = AccountUsagePlanningHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_percentage_equal_to_the_threshold_at_dispatch_stops_the_attempt(harness);
    }

    [Fact]
    public async Task A_healthy_account_dispatches_and_the_provider_runs_once()
    {
        var (provider, harness) = AccountUsagePlanningHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_healthy_account_dispatches_and_the_provider_runs_once(harness);
    }

    [Fact]
    public async Task A_refusal_that_cannot_be_recorded_fails_closed_and_later_below_threshold_observations_never_dispatch_it()
    {
        var (provider, harness) = AccountUsagePlanningHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_refusal_that_cannot_be_recorded_fails_closed_and_later_below_threshold_observations_never_dispatch_it(harness);
    }
}
