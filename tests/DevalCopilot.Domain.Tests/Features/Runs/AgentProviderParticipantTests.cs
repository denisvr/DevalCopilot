using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AgentProviderParticipantTests
{
    [Theory]
    [InlineData(AgentProvider.Codex, ParticipantKind.Codex)]
    [InlineData(AgentProvider.ClaudeCode, ParticipantKind.Claude)]
    public void For_maps_each_defined_provider_to_its_exact_participant(AgentProvider provider, ParticipantKind expected)
    {
        Assert.Equal(expected, AgentProviderParticipant.For(provider));
    }

    [Fact]
    public void For_throws_for_an_undefined_provider()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentProviderParticipant.For((AgentProvider)999));
    }
}
