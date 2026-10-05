using DevalCopilot.Application.Features.Projects.Ports;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>The run-scoped Codex account-usage stop (ADR-0025) through the real challenge-resolution supervisor (see the planning class).</summary>
public sealed partial class ChallengeResolutionSupervisorHostedTests
{
    private (ServiceProvider Provider, AccountUsageHostedHarness Harness) AccountUsageResolutionHost()
    {
        var scripted = new ScriptedAccountUsageAdapter();
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var agent = new FakeChallengeResolutionAdapter(_artifactStore);
        var provider = BuildServiceProvider(evidenceReader, agent, scripted);
        var harness = new AccountUsageHostedHarness(
            scripted,
            async () =>
            {
                var (runId, attemptId, _, _, challengeIds) =
                    await SeedEligibleChallengeResolutionAttemptAsync(provider, evidenceReader, AccountUsageHostedScenarios.Threshold);
                agent.FinalResponseJsonToWrite = ResolvedFinalResponseJson(challengeIds);
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
        var (provider, harness) = AccountUsageResolutionHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_stop_reached_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider(harness, unavailable: false);
    }

    [Fact]
    public async Task An_account_that_cannot_be_observed_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider()
    {
        var (provider, harness) = AccountUsageResolutionHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_stop_reached_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider(harness, unavailable: true);
    }

    [Fact]
    public async Task A_percentage_equal_to_the_threshold_at_dispatch_stops_the_attempt()
    {
        var (provider, harness) = AccountUsageResolutionHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_percentage_equal_to_the_threshold_at_dispatch_stops_the_attempt(harness);
    }

    [Fact]
    public async Task A_healthy_account_dispatches_and_the_provider_runs_once()
    {
        var (provider, harness) = AccountUsageResolutionHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_healthy_account_dispatches_and_the_provider_runs_once(harness);
    }

    [Fact]
    public async Task A_refusal_that_cannot_be_recorded_fails_closed_and_later_below_threshold_observations_never_dispatch_it()
    {
        var (provider, harness) = AccountUsageResolutionHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_refusal_that_cannot_be_recorded_fails_closed_and_later_below_threshold_observations_never_dispatch_it(harness);
    }
}
