using System.Reflection;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// <see cref="AgentAttemptIdentity.IsCoherent(Attempt)"/> now delegates to a scalar overload that a bounded reader can call without
/// an <see cref="Attempt"/>. The ORIGINAL rule is restated here independently as an oracle, and the attempt overload and the scalar
/// overload (fed through <see cref="AgentAssignmentSnapshotPolicy"/>) must agree with it on a deterministic sample of every fact it reads.
/// </summary>
public sealed class AgentAttemptIdentityScalarEquivalenceTests
{
    private static readonly AttemptKind[] Kinds = [AttemptKind.Simulated, AttemptKind.Process, AttemptKind.Agent, (AttemptKind)99];
    private static readonly AttemptStatus?[] Statuses = [AttemptStatus.Running, AttemptStatus.Completed, AttemptStatus.Failed, AttemptStatus.Interrupted, (AttemptStatus)99, null];
    private static readonly AgentRole?[] Roles =
        [null, AgentRole.Planner, AgentRole.CriticalReviewer, AgentRole.Resolver, AgentRole.Implementer, AgentRole.CodeReviewer, (AgentRole)99];
    private static readonly AgentProvider?[] Providers = [null, AgentProvider.Codex, AgentProvider.ClaudeCode, (AgentProvider)99];
    private static readonly AgentResponseContract?[] Contracts =
    [
        null, AgentResponseContract.Proposal, AgentResponseContract.CriticalReview, AgentResponseContract.ChallengeResolution,
        AgentResponseContract.ImplementationReport, AgentResponseContract.ImplementationReview, AgentResponseContract.ReviewCorrection,
        AgentResponseContract.VerificationDiagnosis, (AgentResponseContract)99,
    ];
    private static readonly string?[] Identifiers = [null, "ok", string.Empty, new string('m', 128), new string('m', 129)];
    private static readonly string?[] StoredTurns = [null, "7", "0", "101", "t:7", "b:37", "r:401C000000000000"];

    private sealed record Facts(
        AttemptKind Kind,
        AttemptStatus? Status,
        AgentRole? Role,
        AgentProvider? Provider,
        AgentResponseContract? Contract,
        string? RequestedModel,
        string? StoredTurn);

    private static bool Supported(AgentRole role, AgentProvider provider) => (role, provider) switch
    {
        (AgentRole.Planner, AgentProvider.Codex) => true,
        (AgentRole.Resolver, AgentProvider.Codex) => true,
        (AgentRole.CodeReviewer, AgentProvider.Codex) => true,
        (AgentRole.CriticalReviewer, AgentProvider.ClaudeCode) => true,
        (AgentRole.Implementer, AgentProvider.ClaudeCode) => true,
        _ => false,
    };

    private static AgentAssignmentSnapshot? Snapshot(Facts facts) => AgentAssignmentSnapshotPolicy.Read(
        facts.Kind, facts.Provider, facts.RequestedModel, null, null, null, AgentPermissionProfile.WorkspaceEditOnly, "contract-v2",
        ClaudeMutationTurnLimit.Read(facts.StoredTurn));

    // The original rule, restated: every fact is read, defined, supported, role-matching, and the snapshot is well-formed.
    private static bool Oracle(Facts facts)
    {
        if (facts.Kind != AttemptKind.Agent
            || facts.Status is not { } status
            || !Enum.IsDefined(status)
            || facts.Role is not { } role
            || facts.Provider is not { } provider
            || facts.Contract is not { } contract
            || !Enum.IsDefined(role)
            || !Enum.IsDefined(provider)
            || !Enum.IsDefined(contract)
            || !Supported(role, provider))
        {
            return false;
        }

        return AgentAttemptContract.For(contract).Role == role && Snapshot(facts) is not null;
    }

    private static Attempt AttemptWith(Facts facts)
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 65536, 131072, DateTimeOffset.UtcNow, 1);
        void Set(string property, object? value) => typeof(Attempt).GetProperty(property)!.SetValue(attempt, value);
        Set(nameof(Attempt.Kind), facts.Kind);
        // The attempt has a non-null status; an absent status is only expressible through the scalar overload.
        Set(nameof(Attempt.Status), facts.Status ?? (AttemptStatus)99);
        Set(nameof(Attempt.AgentRole), facts.Role);
        Set(nameof(Attempt.AgentProvider), facts.Provider);
        Set(nameof(Attempt.AgentResponseContract), facts.Contract);
        Set(nameof(Attempt.AgentRequestedModel), facts.RequestedModel);
        Set(nameof(Attempt.AgentPermissionProfile), AgentPermissionProfile.WorkspaceEditOnly);
        Set(nameof(Attempt.AgentAdapterContractVersion), "contract-v2");
        typeof(Attempt).GetField(Attempt.AgentRequestedMaxTurnsStorageProperty, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(attempt, facts.StoredTurn);
        return attempt;
    }

    private static Facts Valid() => new(
        AttemptKind.Agent, AttemptStatus.Completed, AgentRole.Implementer, AgentProvider.ClaudeCode, AgentResponseContract.ImplementationReport,
        "sonnet", "7");

    private static void AssertEquivalent(Facts facts)
    {
        var expected = Oracle(facts);

        Assert.Equal(expected, AgentAttemptIdentity.IsCoherent(
            facts.Kind, facts.Status, facts.Role, facts.Provider, facts.Contract, Snapshot(facts)));
        Assert.Equal(expected, AgentAttemptIdentity.IsCoherent(AttemptWith(facts)));
    }

    [Fact]
    public void The_valid_baseline_is_coherent_through_every_overload()
    {
        Assert.True(Oracle(Valid()));
        AssertEquivalent(Valid());
    }

    [Fact]
    public void Every_single_factor_variation_agrees_with_the_original_rule()
    {
        var facts = Valid();
        var variations = new List<Facts>();
        variations.AddRange(Kinds.Select(value => facts with { Kind = value }));
        variations.AddRange(Statuses.Select(value => facts with { Status = value }));
        variations.AddRange(Roles.Select(value => facts with { Role = value }));
        variations.AddRange(Providers.Select(value => facts with { Provider = value }));
        variations.AddRange(Contracts.Select(value => facts with { Contract = value }));
        variations.AddRange(Identifiers.Select(value => facts with { RequestedModel = value }));
        variations.AddRange(StoredTurns.Select(value => facts with { StoredTurn = value }));

        Assert.Contains(variations, candidate => Oracle(candidate));
        Assert.Contains(variations, candidate => !Oracle(candidate));
        foreach (var candidate in variations)
        {
            AssertEquivalent(candidate);
        }
    }

    [Fact]
    public void A_deterministic_sample_of_combinations_agrees_with_the_original_rule()
    {
        var state = 37u;
        int Next(int bound)
        {
            state = unchecked((state * 1664525u) + 1013904223u);
            return (int)((state >> 8) % (uint)bound);
        }

        T Pick<T>(IReadOnlyList<T> values) => values[Next(values.Count)];
        var coherent = 0;
        var incoherent = 0;
        for (var index = 0; index < 20_000; index++)
        {
            var role = Next(3) == 0 ? Pick(Roles) : Pick(Roles.Where(value => value is >= AgentRole.Planner and <= AgentRole.CodeReviewer).ToArray());
            var provider = Next(3) == 0 ? Pick(Providers) : (role is AgentRole.Planner or AgentRole.Resolver or AgentRole.CodeReviewer
                ? AgentProvider.Codex
                : AgentProvider.ClaudeCode);
            var contract = Next(2) == 0 && role is { } definedRole && Enum.IsDefined(definedRole)
                ? ContractOf(definedRole)
                : Pick(Contracts);
            var facts = new Facts(
                Next(5) == 0 ? Pick(Kinds) : AttemptKind.Agent,
                Next(5) == 0 ? Pick(Statuses) : AttemptStatus.Completed,
                role,
                provider,
                contract,
                Next(4) == 0 ? Pick(Identifiers) : "sonnet",
                Next(4) == 0 ? Pick(StoredTurns) : "7");
            AssertEquivalent(facts);
            if (Oracle(facts))
            {
                coherent++;
            }
            else
            {
                incoherent++;
            }
        }

        Assert.True(coherent > 1_000 && incoherent > 1_000, $"coherent {coherent}, incoherent {incoherent}");
    }

    private static AgentResponseContract ContractOf(AgentRole role) => role switch
    {
        AgentRole.Planner => AgentResponseContract.Proposal,
        AgentRole.CriticalReviewer => AgentResponseContract.CriticalReview,
        AgentRole.Resolver => AgentResponseContract.ChallengeResolution,
        AgentRole.Implementer => AgentResponseContract.ImplementationReport,
        _ => AgentResponseContract.ImplementationReview,
    };
}
