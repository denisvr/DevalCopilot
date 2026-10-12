using System.Reflection;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>
/// The assignment-snapshot read rule was extracted from <see cref="Attempt.GetAssignmentSnapshot"/> into
/// <see cref="AgentAssignmentSnapshotPolicy"/> so a bounded scalar reader can share it. These tests restate the ORIGINAL
/// implementation independently as an oracle and prove that the attempt method and the policy agree with it, and with each
/// other, on every single-factor variation and on a large deterministic sample of combinations of every stored fact the rule reads.
/// </summary>
public sealed class AgentAssignmentSnapshotPolicyTests
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly AttemptKind[] Kinds = [AttemptKind.Simulated, AttemptKind.Process, AttemptKind.Agent, (AttemptKind)99];
    private static readonly AgentProvider?[] Providers = [null, AgentProvider.Codex, AgentProvider.ClaudeCode, (AgentProvider)99];
    private static readonly string?[] Identifiers = [null, string.Empty, " ", "\t", "ok", new string('a', 128), new string('a', 129), " a ", "ünï"];
    private static readonly string?[] Versions = [null, string.Empty, "  ", "contract-v2", new string('v', 128), new string('v', 129)];
    private static readonly AgentPermissionProfile?[] Profiles =
        [null, AgentPermissionProfile.Unknown, AgentPermissionProfile.WorkspaceEditOnly, AgentPermissionProfile.ReadOnly, (AgentPermissionProfile)99];
    private static readonly string?[] StoredTurns = [null, "1", "7", "100", "0", "101", "-1", "07", "1e1", "3.5", "t:7", "r:401C000000000000", "b:37", "?:Byte", string.Empty];

    private sealed record Facts(
        AttemptKind Kind,
        AgentProvider? Provider,
        string? RequestedModel,
        string? ObservedModel,
        string? RequestedEffort,
        string? ObservedEffort,
        AgentPermissionProfile? Profile,
        string? Version,
        string? StoredTurn);

    private static readonly Facts Baseline = new(
        AttemptKind.Agent, AgentProvider.ClaudeCode, "sonnet", null, "high", null, AgentPermissionProfile.WorkspaceEditOnly, "contract-v2", "7");

    private static Attempt AttemptWith(Facts facts)
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 65536, 131072, DateTimeOffset.UtcNow, 1);
        void Set(string property, object? value) => typeof(Attempt).GetProperty(property)!.SetValue(attempt, value);
        Set(nameof(Attempt.Kind), facts.Kind);
        Set(nameof(Attempt.AgentProvider), facts.Provider);
        Set(nameof(Attempt.AgentRequestedModel), facts.RequestedModel);
        Set(nameof(Attempt.AgentObservedModel), facts.ObservedModel);
        Set(nameof(Attempt.AgentRequestedEffort), facts.RequestedEffort);
        Set(nameof(Attempt.AgentObservedEffort), facts.ObservedEffort);
        Set(nameof(Attempt.AgentPermissionProfile), facts.Profile);
        Set(nameof(Attempt.AgentAdapterContractVersion), facts.Version);
        typeof(Attempt).GetField(Attempt.AgentRequestedMaxTurnsStorageProperty, Private)!.SetValue(attempt, facts.StoredTurn);
        return attempt;
    }

    // The original Attempt.GetAssignmentSnapshot, restated verbatim (private identifier rule included) as an independent oracle.
    private static bool OldIsValidIdentifier(string? value) => value is null || (!string.IsNullOrWhiteSpace(value) && value.Length <= 128);

    private static AgentAssignmentSnapshot? Oracle(Facts facts)
    {
        var turns = ClaudeMutationTurnLimit.Read(facts.StoredTurn);
        if (facts.Kind != AttemptKind.Agent
            || facts.Provider is not { } provider
            || !Enum.IsDefined(provider)
            || !OldIsValidIdentifier(facts.RequestedModel)
            || !OldIsValidIdentifier(facts.ObservedModel)
            || !OldIsValidIdentifier(facts.RequestedEffort)
            || !OldIsValidIdentifier(facts.ObservedEffort)
            || turns.IsMalformed
            || (facts.Version is { } contractVersion && (string.IsNullOrWhiteSpace(contractVersion) || contractVersion.Length > 128)))
        {
            return null;
        }

        var permissionProfile = facts.Profile ?? AgentPermissionProfile.Unknown;
        if (!Enum.IsDefined(permissionProfile))
        {
            return null;
        }

        return new AgentAssignmentSnapshot(
            provider, facts.RequestedModel, facts.ObservedModel, facts.RequestedEffort, facts.ObservedEffort, permissionProfile, facts.Version,
            turns.Value);
    }

    private static AgentAssignmentSnapshot? Policy(Facts facts) => AgentAssignmentSnapshotPolicy.Read(
        facts.Kind, facts.Provider, facts.RequestedModel, facts.ObservedModel, facts.RequestedEffort, facts.ObservedEffort, facts.Profile,
        facts.Version, ClaudeMutationTurnLimit.Read(facts.StoredTurn));

    private static void AssertEquivalent(Facts facts)
    {
        var expected = Oracle(facts);

        Assert.Equal(expected, AttemptWith(facts).GetAssignmentSnapshot());
        Assert.Equal(expected, Policy(facts));
    }

    [Fact]
    public void The_baseline_is_a_valid_snapshot_so_every_variation_below_is_meaningful()
    {
        Assert.NotNull(Oracle(Baseline));
        AssertEquivalent(Baseline);
    }

    [Fact]
    public void Every_single_factor_variation_is_equivalent_to_the_original_rule()
    {
        var variations = new List<Facts>();
        variations.AddRange(Kinds.Select(value => Baseline with { Kind = value }));
        variations.AddRange(Providers.Select(value => Baseline with { Provider = value }));
        variations.AddRange(Identifiers.Select(value => Baseline with { RequestedModel = value }));
        variations.AddRange(Identifiers.Select(value => Baseline with { ObservedModel = value }));
        variations.AddRange(Identifiers.Select(value => Baseline with { RequestedEffort = value }));
        variations.AddRange(Identifiers.Select(value => Baseline with { ObservedEffort = value }));
        variations.AddRange(Profiles.Select(value => Baseline with { Profile = value }));
        variations.AddRange(Versions.Select(value => Baseline with { Version = value }));
        variations.AddRange(StoredTurns.Select(value => Baseline with { StoredTurn = value }));

        Assert.True(variations.Count > 60);
        Assert.Contains(variations, facts => Oracle(facts) is null);
        Assert.Contains(variations, facts => Oracle(facts) is not null);
        foreach (var facts in variations)
        {
            AssertEquivalent(facts);
        }
    }

    [Fact]
    public void A_deterministic_sample_of_combinations_of_every_stored_fact_is_equivalent_to_the_original_rule()
    {
        // A fixed linear congruential sequence: deterministic, and not a security-sensitive random source.
        var state = 36u;
        int Next(int bound)
        {
            state = unchecked((state * 1664525u) + 1013904223u);
            return (int)((state >> 8) % (uint)bound);
        }

        T Pick<T>(IReadOnlyList<T> values) => values[Next(values.Count)];
        var valid = 0;
        var invalid = 0;
        for (var index = 0; index < 20_000; index++)
        {
            // Most samples keep the Agent kind so the interesting branches are reached, and a quarter reuse the valid identifiers.
            var facts = new Facts(
                Next(4) == 0 ? Pick(Kinds) : AttemptKind.Agent,
                Next(3) == 0 ? Pick(Providers) : AgentProvider.ClaudeCode,
                Next(4) == 0 ? Pick(Identifiers) : "sonnet",
                Next(4) == 0 ? Pick(Identifiers) : null,
                Next(4) == 0 ? Pick(Identifiers) : "high",
                Next(4) == 0 ? Pick(Identifiers) : null,
                Next(3) == 0 ? Pick(Profiles) : AgentPermissionProfile.WorkspaceEditOnly,
                Next(3) == 0 ? Pick(Versions) : "contract-v2",
                Next(3) == 0 ? Pick(StoredTurns) : "7");
            AssertEquivalent(facts);
            if (Oracle(facts) is null)
            {
                invalid++;
            }
            else
            {
                valid++;
            }
        }

        Assert.True(valid > 1_000 && invalid > 1_000, $"valid {valid}, invalid {invalid}");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("a", true)]
    public void The_identifier_rule_is_exactly_absent_or_nonblank_and_bounded(string? value, bool expected) =>
        Assert.Equal(expected, AgentAssignmentSnapshotPolicy.IsValidIdentifier(value));

    [Fact]
    public void The_identifier_bound_is_one_hundred_twenty_eight_characters_exactly()
    {
        Assert.Equal(128, AgentAssignmentSnapshotPolicy.MaximumIdentifierLength);
        Assert.True(AgentAssignmentSnapshotPolicy.IsValidIdentifier(new string('x', 128)));
        Assert.False(AgentAssignmentSnapshotPolicy.IsValidIdentifier(new string('x', 129)));
    }
}
