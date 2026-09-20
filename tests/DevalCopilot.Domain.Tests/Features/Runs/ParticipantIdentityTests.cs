using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class ParticipantIdentityTests
{
    [Theory]
    [InlineData(AgentProvider.Codex)]
    [InlineData(AgentProvider.ClaudeCode)]
    public void ForAgentWithUnknownRole_preserves_provider_without_inventing_a_role(AgentProvider provider)
    {
        var identity = ParticipantIdentity.ForAgentWithUnknownRole(provider);

        Assert.Equal(ParticipantKind.Agent, identity.Kind);
        Assert.Null(identity.Role);
        Assert.Equal(provider, identity.Provider);
    }

    [Fact]
    public void ForAgent_preserves_the_complete_role_and_provider_identity()
    {
        var identity = ParticipantIdentity.ForAgent(AgentRole.Resolver, AgentProvider.Codex);

        Assert.Equal(ParticipantKind.Agent, identity.Kind);
        Assert.Equal(AgentRole.Resolver, identity.Role);
        Assert.Equal(AgentProvider.Codex, identity.Provider);
    }

    [Fact]
    public void FromParts_rejects_agent_without_provider()
    {
        Assert.Throws<ArgumentException>(
            () => ParticipantIdentity.FromParts(ParticipantKind.Agent, AgentRole.Planner, provider: null));
    }

    [Fact]
    public void FromParts_rejects_agent_metadata_on_a_non_agent_kind()
    {
        Assert.Throws<ArgumentException>(
            () => ParticipantIdentity.FromParts(
                ParticipantKind.Human,
                AgentRole.Planner,
                AgentProvider.Codex));
    }

    [Fact]
    public void Factories_reject_undefined_role_and_provider_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParticipantIdentity.ForAgentWithUnknownRole((AgentProvider)999));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParticipantIdentity.ForAgent((AgentRole)999, AgentProvider.Codex));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParticipantIdentity.ForAgent(AgentRole.Planner, (AgentProvider)999));
    }
}
