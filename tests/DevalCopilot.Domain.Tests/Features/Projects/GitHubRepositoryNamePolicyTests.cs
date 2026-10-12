using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

public sealed class GitHubRepositoryNamePolicyTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("A1")]
    [InlineData("octo-cat")]
    [InlineData("o-c-t-0")]
    public void An_owner_of_letters_digits_and_inner_hyphens_is_admitted(string owner) =>
        Assert.True(GitHubRepositoryNamePolicy.IsOwner(owner));

    [Fact]
    public void An_owner_of_exactly_thirty_nine_characters_is_admitted_and_forty_is_not()
    {
        Assert.True(GitHubRepositoryNamePolicy.IsOwner(new string('a', 39)));
        Assert.False(GitHubRepositoryNamePolicy.IsOwner(new string('a', 40)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-a")]
    [InlineData("a-")]
    [InlineData("a_b")]
    [InlineData("a.b")]
    [InlineData("a b")]
    [InlineData(" a")]
    [InlineData("a\n")]
    [InlineData("a/b")]
    [InlineData("é")]
    [InlineData("Ａ")]
    public void An_owner_outside_the_conservative_ascii_subset_is_refused(string owner) =>
        Assert.False(GitHubRepositoryNamePolicy.IsOwner(owner));

    [Fact]
    public void A_null_name_is_refused_without_throwing()
    {
        Assert.False(GitHubRepositoryNamePolicy.IsOwner(null));
        Assert.False(GitHubRepositoryNamePolicy.IsRepository(null));
    }

    [Theory]
    [InlineData("r")]
    [InlineData("repo.name")]
    [InlineData("repo_name-1")]
    [InlineData("repo.git")]
    [InlineData("1")]
    public void A_repository_of_letters_digits_dots_underscores_and_hyphens_is_admitted(string repository) =>
        Assert.True(GitHubRepositoryNamePolicy.IsRepository(repository));

    [Fact]
    public void A_repository_of_exactly_one_hundred_characters_is_admitted_and_one_hundred_and_one_is_not()
    {
        Assert.True(GitHubRepositoryNamePolicy.IsRepository(new string('r', 100)));
        Assert.False(GitHubRepositoryNamePolicy.IsRepository(new string('r', 101)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(".hidden")]
    [InlineData("-r")]
    [InlineData("_r")]
    [InlineData("r r")]
    [InlineData("r/r")]
    [InlineData("r\t")]
    [InlineData("é")]
    public void A_repository_outside_the_conservative_ascii_subset_is_refused(string repository) =>
        Assert.False(GitHubRepositoryNamePolicy.IsRepository(repository));
}
