using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

public sealed class ProjectTests
{
    [Fact]
    public void ReserveExecutionNumber_returns_monotonically_increasing_values()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\DevalCopilot");

        var first = project.ReserveExecutionNumber();
        var second = project.ReserveExecutionNumber();
        var third = project.ReserveExecutionNumber();

        Assert.Equal([1, 2, 3], new[] { first, second, third });
    }
}
