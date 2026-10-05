using DevalCopilot.Application.Features.Projects.Ports;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>The run-scoped Codex account-usage stop (ADR-0025) through the real implementation-review (code review) supervisor (see the planning class).</summary>
public sealed partial class ImplementationReviewSupervisorHostedTests
{
    private (ServiceProvider Provider, AccountUsageHostedHarness Harness) AccountUsageReviewHost()
    {
        var scripted = new ScriptedAccountUsageAdapter();
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call => call <= 3
            ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint)
            : SequencedGitWorkspaceEvidenceReader.Matching(ResultFingerprint));
        var agent = new FakeImplementationReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = ApprovedFinalResponseJson() };
        var provider = BuildServiceProvider(evidenceReader, agent, accountUsage: scripted);
        var harness = new AccountUsageHostedHarness(
            scripted,
            async () =>
            {
                var (runId, attemptId, _, _, _, _, _) =
                    await SeedEligibleCodeReviewAttemptAsync(provider, evidenceReader, AccountUsageHostedScenarios.Threshold);
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
        var (provider, harness) = AccountUsageReviewHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_stop_reached_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider(harness, unavailable: false);
    }

    [Fact]
    public async Task An_account_that_cannot_be_observed_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider()
    {
        var (provider, harness) = AccountUsageReviewHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_stop_reached_at_dispatch_resolves_the_claimed_attempt_once_and_never_invokes_the_provider(harness, unavailable: true);
    }

    [Fact]
    public async Task A_percentage_equal_to_the_threshold_at_dispatch_stops_the_attempt()
    {
        var (provider, harness) = AccountUsageReviewHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_percentage_equal_to_the_threshold_at_dispatch_stops_the_attempt(harness);
    }

    [Fact]
    public async Task A_healthy_account_dispatches_and_the_provider_runs_once()
    {
        var (provider, harness) = AccountUsageReviewHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_healthy_account_dispatches_and_the_provider_runs_once(harness);
    }

    [Fact]
    public async Task A_refusal_that_cannot_be_recorded_fails_closed_and_later_below_threshold_observations_never_dispatch_it()
    {
        var (provider, harness) = AccountUsageReviewHost();
        await using var _ = provider;
        await AccountUsageHostedScenarios.A_refusal_that_cannot_be_recorded_fails_closed_and_later_below_threshold_observations_never_dispatch_it(harness);
    }
}
