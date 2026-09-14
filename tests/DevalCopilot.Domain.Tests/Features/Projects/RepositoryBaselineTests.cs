using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

public sealed class RepositoryBaselineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Sha = new('a', 40);

    [Fact]
    public void Capture_accepts_OnBranch_with_both_a_branch_name_and_a_commit_sha()
    {
        var baseline = RepositoryBaseline.Capture(
            Guid.NewGuid(), Guid.NewGuid(), 1, Now, RepositoryHeadState.OnBranch, "main", Sha, isDirty: false);

        Assert.Equal(RepositoryHeadState.OnBranch, baseline.HeadState);
        Assert.Equal("main", baseline.BranchName);
        Assert.Equal(Sha, baseline.HeadCommitSha);
    }

    [Fact]
    public void Capture_accepts_Unborn_with_a_branch_name_and_no_commit_sha()
    {
        var baseline = RepositoryBaseline.Capture(
            Guid.NewGuid(), Guid.NewGuid(), 1, Now, RepositoryHeadState.Unborn, "main", null, isDirty: false);

        Assert.Equal(RepositoryHeadState.Unborn, baseline.HeadState);
        Assert.Equal("main", baseline.BranchName);
        Assert.Null(baseline.HeadCommitSha);
    }

    [Fact]
    public void Capture_accepts_Detached_with_a_commit_sha_and_no_branch_name()
    {
        var baseline = RepositoryBaseline.Capture(
            Guid.NewGuid(), Guid.NewGuid(), 1, Now, RepositoryHeadState.Detached, null, Sha, isDirty: false);

        Assert.Equal(RepositoryHeadState.Detached, baseline.HeadState);
        Assert.Null(baseline.BranchName);
        Assert.Equal(Sha, baseline.HeadCommitSha);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Capture_accepts_either_dirty_state_regardless_of_head_state(bool isDirty)
    {
        var baseline = RepositoryBaseline.Capture(
            Guid.NewGuid(), Guid.NewGuid(), 1, Now, RepositoryHeadState.OnBranch, "main", Sha, isDirty);

        Assert.Equal(isDirty, baseline.IsDirty);
    }

    [Fact]
    public void Capture_rejects_OnBranch_without_a_branch_name()
    {
        Assert.Throws<ArgumentException>(
            () => RepositoryBaseline.Capture(Guid.NewGuid(), Guid.NewGuid(), 1, Now, RepositoryHeadState.OnBranch, null, Sha, false));
    }

    [Fact]
    public void Capture_rejects_OnBranch_without_a_commit_sha()
    {
        Assert.Throws<ArgumentException>(
            () => RepositoryBaseline.Capture(Guid.NewGuid(), Guid.NewGuid(), 1, Now, RepositoryHeadState.OnBranch, "main", null, false));
    }

    [Fact]
    public void Capture_rejects_Unborn_with_a_commit_sha()
    {
        Assert.Throws<ArgumentException>(
            () => RepositoryBaseline.Capture(Guid.NewGuid(), Guid.NewGuid(), 1, Now, RepositoryHeadState.Unborn, "main", Sha, false));
    }

    [Fact]
    public void Capture_rejects_Unborn_without_a_branch_name()
    {
        Assert.Throws<ArgumentException>(
            () => RepositoryBaseline.Capture(Guid.NewGuid(), Guid.NewGuid(), 1, Now, RepositoryHeadState.Unborn, null, null, false));
    }

    [Fact]
    public void Capture_rejects_Detached_with_a_branch_name()
    {
        Assert.Throws<ArgumentException>(
            () => RepositoryBaseline.Capture(Guid.NewGuid(), Guid.NewGuid(), 1, Now, RepositoryHeadState.Detached, "main", Sha, false));
    }

    [Fact]
    public void Capture_rejects_Detached_without_a_commit_sha()
    {
        Assert.Throws<ArgumentException>(
            () => RepositoryBaseline.Capture(Guid.NewGuid(), Guid.NewGuid(), 1, Now, RepositoryHeadState.Detached, null, null, false));
    }

    [Fact]
    public void Capture_rejects_a_baseline_number_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RepositoryBaseline.Capture(Guid.NewGuid(), Guid.NewGuid(), 0, Now, RepositoryHeadState.OnBranch, "main", Sha, false));
    }
}
