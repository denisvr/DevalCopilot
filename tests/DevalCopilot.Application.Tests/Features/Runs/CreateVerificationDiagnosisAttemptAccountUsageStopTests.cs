using DevalCopilot.Application.Features.Projects.Ports;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The run-scoped Codex account-usage stop (ADR-0025) at the verification-diagnosis claim seam: the shared contract proved
/// against the real claim handler on real file-backed SQLite.</summary>
public sealed class CreateVerificationDiagnosisAttemptAccountUsageStopTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();
    private DiagnosisTestScene? _scene;

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private AccountUsageClaimHarness AccountUsage => new()
    {
        Fixture = _fixture,
        Now = RepairTestScene.Now,
        SeedRunAsync = async () =>
        {
            _scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
            return _scene.Run.Id;
        },
        ClaimOn = async (runId, adapter, clock, hook) =>
        {
            var reader = _scene!.Reader();
            reader.OnFirstCapture = hook;
            var store = _scene.Store;
            var result = await _scene.ClaimAsync(reader: reader, store: store, runId: runId, accountUsageAdapter: adapter, clock: clock);
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
}
