using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class AgentAttemptIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static Attempt Claim(int kind)
    {
        var (id, run, ws, cp, manifest) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var timeout = TimeSpan.FromMinutes(10);
        return kind switch
        {
            0 => Attempt.ClaimAgent(id, run, 1, ws, cp, Fingerprint, manifest, timeout, 1024, 2048, Now, 1),
            1 => Attempt.ClaimAgentCriticalReview(id, run, 1, ws, cp, Fingerprint, manifest, timeout, 1024, 2048, Now, 1),
            2 => Attempt.ClaimAgentChallengeResolution(id, run, 1, ws, cp, Fingerprint, manifest, timeout, 1024, 2048, Now, 1),
            3 => Attempt.ClaimAgentImplementation(id, run, 1, ws, cp, Fingerprint, manifest, timeout, 1024, 2048, Now, 1),
            4 => Attempt.ClaimAgentCodeReview(id, run, 1, ws, cp, Fingerprint, manifest, timeout, 1024, 2048, Now, 1),
            _ => Attempt.ClaimAgentReviewCorrection(id, run, 1, ws, cp, Fingerprint, manifest, timeout, 1024, 2048, Now, 1),
        };
    }

    private static void SetStoredTurnLimit(Attempt attempt, string stored) =>
        typeof(Attempt).GetField(Attempt.AgentRequestedMaxTurnsStorageProperty, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(attempt, stored);

    private static void Set<TValue>(Attempt attempt, string property, TValue value) =>
        typeof(Attempt).GetProperty(property)!.SetValue(attempt, value);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Every_supported_role_provider_pair_claim_is_coherent(int kind) =>
        Assert.True(AgentAttemptIdentity.IsCoherent(Claim(kind)));

    [Fact]
    public void A_non_agent_attempt_is_not_coherent() =>
        Assert.False(AgentAttemptIdentity.IsCoherent(Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, Now)));

    [Fact]
    public void An_unsupported_role_provider_pair_is_not_coherent()
    {
        var attempt = Claim(0);
        Set(attempt, nameof(Attempt.AgentProvider), (AgentProvider?)AgentProvider.ClaudeCode);

        Assert.False(AgentAttemptIdentity.IsCoherent(attempt));
    }

    [Fact]
    public void A_missing_or_undefined_role_provider_or_contract_is_not_coherent()
    {
        var noRole = Claim(0);
        Set(noRole, nameof(Attempt.AgentRole), (AgentRole?)null);
        var badRole = Claim(0);
        Set(badRole, nameof(Attempt.AgentRole), (AgentRole?)(AgentRole)99);
        var noProvider = Claim(0);
        Set(noProvider, nameof(Attempt.AgentProvider), (AgentProvider?)null);
        var badProvider = Claim(0);
        Set(badProvider, nameof(Attempt.AgentProvider), (AgentProvider?)(AgentProvider)99);
        var noContract = Claim(0);
        Set(noContract, nameof(Attempt.AgentResponseContract), (AgentResponseContract?)null);
        var badContract = Claim(0);
        Set(badContract, nameof(Attempt.AgentResponseContract), (AgentResponseContract?)(AgentResponseContract)99);

        Assert.All(
            new[] { noRole, badRole, noProvider, badProvider, noContract, badContract },
            attempt => Assert.False(AgentAttemptIdentity.IsCoherent(attempt)));
    }

    [Fact]
    public void A_contract_that_belongs_to_a_different_role_is_not_coherent()
    {
        var attempt = Claim(0);
        Set(attempt, nameof(Attempt.AgentResponseContract), (AgentResponseContract?)AgentResponseContract.ReviewCorrection);

        Assert.False(AgentAttemptIdentity.IsCoherent(attempt));
    }

    [Fact]
    public void An_oversized_assignment_identifier_or_undefined_status_is_not_coherent()
    {
        var oversized = Claim(3);
        Set(oversized, nameof(Attempt.AgentRequestedModel), new string('m', 129));
        var badStatus = Claim(0);
        Set(badStatus, nameof(Attempt.Status), (AttemptStatus)99);

        Assert.False(AgentAttemptIdentity.IsCoherent(oversized));
        Assert.False(AgentAttemptIdentity.IsCoherent(badStatus));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void A_stored_out_of_range_turn_limit_is_not_coherent_but_a_valid_one_is(int kind)
    {
        var valid = Claim(kind);
        SetStoredTurnLimit(valid, "100");
        Assert.True(AgentAttemptIdentity.IsCoherent(valid));

        foreach (var stored in new[] { "0", "-1", "101", "2147483647", "3.5", "4294967297", "abc", "" })
        {
            var attempt = Claim(kind);
            SetStoredTurnLimit(attempt, stored);
            Assert.False(AgentAttemptIdentity.IsCoherent(attempt));
        }
    }
}
