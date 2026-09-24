using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AgentAssignmentTests
{
    [Fact]
    public void Claim_persists_the_Claude_assignment_without_inventing_observations()
    {
        var attempt = Claim("claude-model", "high");

        var assignment = attempt.GetAssignmentSnapshot();
        Assert.NotNull(assignment);
        Assert.Equal(AgentProvider.ClaudeCode, assignment.Provider);
        Assert.Equal("claude-model", assignment.RequestedModel);
        Assert.Equal("high", assignment.RequestedEffort);
        Assert.Null(assignment.ObservedModel);
        Assert.Null(assignment.ObservedEffort);
        Assert.Equal(AgentPermissionProfile.WorkspaceEditOnly, assignment.PermissionProfile);
        Assert.Equal("claude-implementation-v1", assignment.AdapterContractVersion);
    }

    [Fact]
    public void Provider_observations_are_write_once_and_do_not_change_requested_assignment()
    {
        var attempt = Claim("requested-model", "requested-effort");

        attempt.RecordAgentObservedAssignment("observed-model", "observed-effort");

        var assignment = attempt.GetAssignmentSnapshot();
        Assert.NotNull(assignment);
        Assert.Equal("requested-model", assignment.RequestedModel);
        Assert.Equal("requested-effort", assignment.RequestedEffort);
        Assert.Equal("observed-model", assignment.ObservedModel);
        Assert.Equal("observed-effort", assignment.ObservedEffort);
        Assert.Throws<InvalidOperationException>(() => attempt.RecordAgentObservedAssignment("other-model", null));
    }

    [Fact]
    public void A_provider_that_reports_no_assignment_facts_leaves_observations_unknown()
    {
        var attempt = Claim(null, null);

        attempt.RecordAgentObservedAssignment(null, null);

        var assignment = attempt.GetAssignmentSnapshot();
        Assert.NotNull(assignment);
        Assert.Null(assignment.ObservedModel);
        Assert.Null(assignment.ObservedEffort);
    }

    [Fact]
    public void Invalid_provider_profile_contract_and_assignment_identifiers_fail_closed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentImplementationWithAssignment(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 65536, 131072, DateTimeOffset.UtcNow, null, null,
            (AgentPermissionProfile)999, "claude-implementation-v1", 1));
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentImplementationWithAssignment(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 65536, 131072, DateTimeOffset.UtcNow, null, null,
            AgentPermissionProfile.WorkspaceEditOnly, " ", 1));
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentImplementationWithAssignment(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 65536, 131072, DateTimeOffset.UtcNow, new string('m', 129), null,
            AgentPermissionProfile.WorkspaceEditOnly, "claude-implementation-v1", 1));
    }

    [Fact]
    public void Historical_assignment_values_remain_explicitly_unknown()
    {
        var historical = Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 65536, 131072, DateTimeOffset.UtcNow, 1);

        var assignment = historical.GetAssignmentSnapshot();

        Assert.NotNull(assignment);
        Assert.Equal(AgentProvider.Codex, assignment.Provider);
        Assert.Null(assignment.RequestedModel);
        Assert.Null(assignment.ObservedModel);
        Assert.Null(assignment.RequestedEffort);
        Assert.Null(assignment.ObservedEffort);
        Assert.Equal(AgentPermissionProfile.Unknown, assignment.PermissionProfile);
        Assert.Null(assignment.AdapterContractVersion);
    }

    [Fact]
    public void Invalid_persisted_provider_profile_or_contract_is_not_projected()
    {
        var attempt = Claim("model", "effort");

        SetPrivateProperty(attempt, nameof(Attempt.AgentProvider), (AgentProvider)999);
        Assert.Null(attempt.GetAssignmentSnapshot());

        attempt = Claim("model", "effort");
        SetPrivateProperty(attempt, nameof(Attempt.AgentPermissionProfile), (AgentPermissionProfile)999);
        Assert.Null(attempt.GetAssignmentSnapshot());

        attempt = Claim("model", "effort");
        SetPrivateProperty(attempt, nameof(Attempt.AgentAdapterContractVersion), new string('v', 129));
        Assert.Null(attempt.GetAssignmentSnapshot());
    }

    [Fact]
    public void Adapter_contract_version_is_bounded_at_claim_time()
    {
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentImplementationWithAssignment(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 65536, 131072, DateTimeOffset.UtcNow, null, null,
            AgentPermissionProfile.WorkspaceEditOnly, new string('v', 129), 1));
    }

    private static Attempt Claim(string? requestedModel, string? requestedEffort) =>
        Attempt.ClaimAgentImplementationWithAssignment(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 65536, 131072, DateTimeOffset.UtcNow, requestedModel,
            requestedEffort, AgentPermissionProfile.WorkspaceEditOnly,
            "claude-implementation-v1", 1);

    private static void SetPrivateProperty<TValue>(Attempt attempt, string propertyName, TValue value) =>
        typeof(Attempt).GetProperty(propertyName)!.SetValue(attempt, value);
}
