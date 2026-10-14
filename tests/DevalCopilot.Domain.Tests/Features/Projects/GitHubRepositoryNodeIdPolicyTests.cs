using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

public sealed class GitHubRepositoryNodeIdPolicyTests
{
    [Theory]
    [InlineData("R")]
    [InlineData("R_kgDOExample01")]
    [InlineData("MDEwOlJlcG9zaXRvcnkx")]
    [InlineData("abc-DEF_012/+=")]
    public void An_opaque_id_of_the_conservative_character_set_is_admitted(string id) =>
        Assert.True(GitHubRepositoryNodeIdPolicy.IsNodeId(id));

    [Fact]
    public void An_id_of_exactly_two_hundred_fifty_six_characters_is_admitted_and_two_hundred_fifty_seven_is_not()
    {
        Assert.True(GitHubRepositoryNodeIdPolicy.IsNodeId(new string('a', 256)));
        Assert.False(GitHubRepositoryNodeIdPolicy.IsNodeId(new string('a', 257)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" R_x")]
    [InlineData("R_x ")]
    [InlineData("R x")]
    [InlineData("R.x")]
    [InlineData("R:x")]
    [InlineData("R\\x")]
    [InlineData("R\"x")]
    [InlineData("R\nx")]
    [InlineData("R\0x")]
    [InlineData("R_\u00e9")]
    [InlineData("R_\uff21")]
    public void Anything_else_is_refused_and_nothing_is_trimmed_or_decoded(string? id) =>
        Assert.False(GitHubRepositoryNodeIdPolicy.IsNodeId(id));
}
