using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

public sealed class WorkspaceBranchNamePolicyTests
{
    [Fact]
    public void Compute_is_deterministic_for_the_same_inputs()
    {
        var projectId = Guid.NewGuid();

        var first = WorkspaceBranchNamePolicy.Compute(projectId, 1);
        var second = WorkspaceBranchNamePolicy.Compute(projectId, 1);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_never_reuses_the_same_name_for_a_different_workspace_number()
    {
        var projectId = Guid.NewGuid();

        var first = WorkspaceBranchNamePolicy.Compute(projectId, 1);
        var second = WorkspaceBranchNamePolicy.Compute(projectId, 2);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Compute_never_collides_across_two_different_projects()
    {
        var first = WorkspaceBranchNamePolicy.Compute(Guid.NewGuid(), 1);
        var second = WorkspaceBranchNamePolicy.Compute(Guid.NewGuid(), 1);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Compute_rejects_a_workspace_number_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkspaceBranchNamePolicy.Compute(Guid.NewGuid(), 0));
    }
}
