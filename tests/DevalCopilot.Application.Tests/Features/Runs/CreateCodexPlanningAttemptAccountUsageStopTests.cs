using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The run-scoped Codex account-usage stop (ADR-0025) at this handler's claim seam: the shared contract proved against the real
/// claim handler on real file-backed SQLite.</summary>
public sealed partial class CreateCodexPlanningAttemptCommandHandlerTests
{
    private AccountUsageClaimHarness AccountUsage => new()
    {
        Fixture = _fixture,
        Now = Now,
        SeedRunAsync = async () => (await SeedStopScenarioAsync()).RunId,
        ClaimOn = async (runId, adapter, clock, hook) =>
        {
            var reader = new AccountUsageEvidenceReader(
                new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null), hook);
            var store = new FakeArtifactStore();
            await using var context = _fixture.CreateContext();
            var handler = new CreateCodexPlanningAttemptCommandHandler(context, reader, store, clock, DurabilityProbe, adapter);
            var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(runId), CancellationToken.None);
            return new AccountUsageClaimOutcome(
                result.IsSuccess, result.IsFailure ? Assert.Single(result.Errors).Code : null, reader.Calls, store.DeletedSealedFiles.Count);
        },
    };

    [Fact]
    public Task A_secondary_window_at_the_threshold_refuses_before_any_external_work() => AccountUsageClaimContract.A_secondary_window_at_the_threshold_refuses_before_any_external_work(AccountUsage);

    [Fact]
    public Task A_primary_window_at_the_threshold_refuses() => AccountUsageClaimContract.A_primary_window_at_the_threshold_refuses(AccountUsage);

    [Fact]
    public Task A_provider_reported_reached_state_refuses_even_with_low_percentages() => AccountUsageClaimContract.A_provider_reported_reached_state_refuses_even_with_low_percentages(AccountUsage);

    [Fact]
    public Task An_unavailable_observation_refuses_without_a_partial_subset() => AccountUsageClaimContract.An_unavailable_observation_refuses_without_a_partial_subset(AccountUsage);

    [Fact]
    public Task Evidence_outside_the_read_interval_or_with_a_passed_reset_refuses() => AccountUsageClaimContract.Evidence_outside_the_read_interval_or_with_a_passed_reset_refuses(AccountUsage);

    [Fact]
    public Task A_value_just_below_the_threshold_is_permitted_and_snapshotted_on_the_attempt() => AccountUsageClaimContract.A_value_just_below_the_threshold_is_permitted_and_snapshotted_on_the_attempt(AccountUsage);

    [Fact]
    public Task A_later_setting_change_does_not_alter_the_claimed_attempts_snapshot() => AccountUsageClaimContract.A_later_setting_change_does_not_alter_the_claimed_attempts_snapshot(AccountUsage);

    [Fact]
    public Task A_disabled_stop_makes_no_observation_and_records_no_snapshot() => AccountUsageClaimContract.A_disabled_stop_makes_no_observation_and_records_no_snapshot(AccountUsage);

    [Fact]
    public Task A_cleared_stop_is_disabled_again() => AccountUsageClaimContract.A_cleared_stop_is_disabled_again(AccountUsage);

    [Fact]
    public Task A_missing_observation_capability_or_a_throwing_adapter_refuses_never_permits() => AccountUsageClaimContract.A_missing_observation_capability_or_a_throwing_adapter_refuses_never_permits(AccountUsage);

    [Fact]
    public Task A_missing_launch_target_refuses_without_an_observation() => AccountUsageClaimContract.A_missing_launch_target_refuses_without_an_observation(AccountUsage);

    [Fact]
    public Task A_stop_enabled_between_the_check_and_the_commit_cannot_be_bypassed() => AccountUsageClaimContract.A_stop_enabled_between_the_check_and_the_commit_cannot_be_bypassed(AccountUsage);

    [Fact]
    public Task A_launch_target_changed_between_the_check_and_the_commit_refuses_and_commits_nothing() => AccountUsageClaimContract.A_launch_target_changed_between_the_check_and_the_commit_refuses_and_commits_nothing(AccountUsage);

    [Fact]
    public Task Evidence_that_is_stale_at_the_commit_seam_refuses_and_commits_nothing() => AccountUsageClaimContract.Evidence_that_is_stale_at_the_commit_seam_refuses_and_commits_nothing(AccountUsage);

    [Fact]
    public Task Evidence_exactly_thirty_seconds_old_at_the_commit_seam_is_still_current() => AccountUsageClaimContract.Evidence_exactly_thirty_seconds_old_at_the_commit_seam_is_still_current(AccountUsage);

    [Theory]
    [InlineData(3.5)]
    [InlineData("abc")]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(4294967297L)]
    public Task A_malformed_stored_setting_refuses_without_an_observation(object stored) =>
        AccountUsageClaimContract.A_malformed_stored_setting_refuses_without_an_observation(AccountUsage, stored);

    [Theory]
    [InlineData(90)]
    [InlineData(null)]
    public Task A_setting_changed_between_the_check_and_the_commit_refuses_and_commits_nothing(int? changeTo) =>
        AccountUsageClaimContract.A_setting_changed_between_the_check_and_the_commit_refuses_and_commits_nothing(AccountUsage, changeTo);

    [Theory]
    [InlineData(3.5)]
    [InlineData("abc")]
    [InlineData(0)]
    [InlineData(4294967297L)]
    public Task A_malformed_advisory_warning_storage_never_affects_the_claim_or_the_stop(object stored) =>
        AccountUsageClaimContract.A_malformed_advisory_warning_storage_never_affects_the_claim_or_the_stop(AccountUsage, stored);

    [Fact]
    public Task A_reached_or_disabled_stop_is_decided_by_the_stop_alone_whatever_the_warning_says() =>
        AccountUsageClaimContract.A_reached_or_disabled_stop_is_decided_by_the_stop_alone_whatever_the_warning_says(AccountUsage);

    [Fact]
    public Task An_advisory_warning_change_during_the_claim_neither_refuses_nor_is_overwritten() =>
        AccountUsageClaimContract.An_advisory_warning_change_during_the_claim_neither_refuses_nor_is_overwritten(AccountUsage);
}
