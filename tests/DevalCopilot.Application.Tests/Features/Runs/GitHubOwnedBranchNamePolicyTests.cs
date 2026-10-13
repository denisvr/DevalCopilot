using DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;
using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class GitHubOwnedBranchNamePolicyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(2147483647)]
    public void Every_branch_the_domain_policy_computes_is_admitted(int workspaceNumber) =>
        Assert.True(GitHubOwnedBranchNamePolicy.IsOwnedBranch(WorkspaceBranchNamePolicy.Compute(Guid.NewGuid(), workspaceNumber)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("main")]
    [InlineData("refs/heads/devalcopilot/workspace/0123456789abcdef0123456789abcdef/3")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/0")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/01")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/-1")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/1a")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/1/2")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/2147483648")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/12345678901")]
    [InlineData("devalcopilot/workspace/0123456789ABCDEF0123456789abcdef/1")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcde/1")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef0/1")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdeg/1")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/\u0661")]
    [InlineData(" devalcopilot/workspace/0123456789abcdef0123456789abcdef/1")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/1 ")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/1\n")]
    public void Anything_else_is_refused_and_nothing_is_trimmed_or_decoded(string? branch) =>
        Assert.False(GitHubOwnedBranchNamePolicy.IsOwnedBranch(branch));
}
