using DevalCopilot.Application.Features.Runs.Ports;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The neutral strict-observation facts (ADR-0025): admitted whole or not at all, bounded, and an owned immutable copy, so
/// nothing a producer or a caller still holds can change what a decision was made from.</summary>
public sealed class CodexAccountUsageObservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static AccountUsageBucket Bucket(string? id = "codex", int primary = 10, int? secondary = null) =>
        new(id, new AccountUsageWindow(primary, null), secondary is { } value ? new AccountUsageWindow(value, null) : null);

    [Fact]
    public void A_valid_snapshot_is_admitted_with_the_host_retrieval_instant_normalized_to_utc()
    {
        var observation = AccountUsageObservation.Create(
            new DateTimeOffset(2026, 10, 4, 14, 0, 0, TimeSpan.FromHours(2)), [Bucket(), Bucket("other", 5, 6)], providerReportedLimitReached: false);

        Assert.True(observation.IsValid);
        Assert.Equal(Now, observation.RetrievedAtUtc);
        Assert.Equal(TimeSpan.Zero, observation.RetrievedAtUtc!.Value.Offset);
        Assert.Equal(2, observation.Buckets.Length);
    }

    [Fact]
    public void The_legacy_single_snapshot_has_no_identifier()
    {
        var observation = AccountUsageObservation.Create(Now, [Bucket(null)], providerReportedLimitReached: false);

        Assert.True(observation.IsValid);
        Assert.Null(observation.Buckets[0].Id);
    }

    [Fact]
    public void Anything_that_is_not_a_complete_valid_snapshot_is_the_one_unavailable_answer()
    {
        var noWindow = new AccountUsageBucket("codex", null, null);
        var over = Enumerable.Range(0, 17).Select(index => Bucket($"b{index}")).ToArray();
        var cases = new AccountUsageObservation[]
        {
            AccountUsageObservation.Create(Now, null, false),
            AccountUsageObservation.Create(Now, [], false),
            AccountUsageObservation.Create(Now, [null], false),
            AccountUsageObservation.Create(Now, [noWindow], false),
            AccountUsageObservation.Create(Now, [Bucket(), noWindow], false),
            AccountUsageObservation.Create(Now, [Bucket(), Bucket()], false),
            AccountUsageObservation.Create(Now, [Bucket("bad id")], false),
            AccountUsageObservation.Create(Now, [Bucket(new string('a', 65))], false),
            AccountUsageObservation.Create(Now, [Bucket(primary: 101)], false),
            AccountUsageObservation.Create(Now, [Bucket(primary: -1)], false),
            AccountUsageObservation.Create(Now, [Bucket(secondary: 101)], false),
            AccountUsageObservation.Create(Now, over, false),
        };

        Assert.All(cases, observation => Assert.Same(AccountUsageObservation.Unavailable, observation));
        Assert.False(AccountUsageObservation.Unavailable.IsValid);
        Assert.Null(AccountUsageObservation.Unavailable.RetrievedAtUtc);
        Assert.Empty(AccountUsageObservation.Unavailable.Buckets);
    }

    [Fact]
    public void Sixteen_buckets_and_a_sixty_four_character_identifier_are_admitted_inclusively()
    {
        var sixteen = Enumerable.Range(0, 16).Select(index => Bucket($"b{index}")).ToArray();

        Assert.True(AccountUsageObservation.Create(Now, sixteen, false).IsValid);
        Assert.True(AccountUsageObservation.Create(Now, [Bucket(new string('a', 64))], false).IsValid);
        Assert.True(AccountUsageObservation.Create(Now, [Bucket("a-b_c.d1")], false).IsValid);
    }

    [Fact]
    public void The_percentage_bounds_zero_and_one_hundred_are_admitted()
    {
        Assert.True(AccountUsageObservation.Create(Now, [Bucket(primary: 0, secondary: 100)], false).IsValid);
    }

    [Fact]
    public void Changing_the_collection_the_producer_supplied_never_changes_the_observation()
    {
        var supplied = new List<AccountUsageBucket?> { Bucket(primary: 10) };
        var observation = AccountUsageObservation.Create(Now, supplied, providerReportedLimitReached: false);

        supplied[0] = Bucket(primary: 99);
        supplied.Add(Bucket("x", 99));
        supplied.Clear();

        Assert.Equal(10, Assert.Single(observation.Buckets).Primary!.UsedPercent);
    }

    [Fact]
    public void The_reached_flag_is_carried_and_defaults_to_unreached()
    {
        Assert.True(AccountUsageObservation.Create(Now, [Bucket()], true).ProviderReportedLimitReached);
        Assert.False(AccountUsageObservation.Create(Now, [Bucket()], false).ProviderReportedLimitReached);
    }
}
