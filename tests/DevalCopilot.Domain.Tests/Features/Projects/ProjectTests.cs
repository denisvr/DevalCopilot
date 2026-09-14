using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

public sealed class ProjectTests
{
    [Fact]
    public void ReserveExecutionNumber_returns_monotonically_increasing_values()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\DevalCopilot", DateTimeOffset.UtcNow);

        var first = project.ReserveExecutionNumber();
        var second = project.ReserveExecutionNumber();
        var third = project.ReserveExecutionNumber();

        Assert.Equal([1, 2, 3], new[] { first, second, third });
    }

    [Fact]
    public void ReserveBaselineNumber_returns_monotonically_increasing_values_starting_at_one()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\DevalCopilot", DateTimeOffset.UtcNow);

        var first = project.ReserveBaselineNumber();
        var second = project.ReserveBaselineNumber();

        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Theory]
    [InlineData(@"C:\repos\DevalCopilot", "C:\\REPOS\\DEVALCOPILOT")]
    [InlineData(@"C:\repos\Foo", "C:\\REPOS\\FOO")]
    public void Register_derives_the_registration_identity_key_as_the_uppercase_invariant_of_the_canonical_path(
        string canonicalPath, string expectedKey)
    {
        var project = Project.Register(Guid.NewGuid(), "Name", canonicalPath, DateTimeOffset.UtcNow);

        Assert.Equal(expectedKey, project.RegistrationIdentityKey);
        Assert.Equal(canonicalPath, project.CanonicalPath);
    }

    [Fact]
    public void Register_rejects_a_non_fully_qualified_canonical_path()
    {
        Assert.Throws<ArgumentException>(
            () => Project.Register(Guid.NewGuid(), "Name", "relative\\path", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Register_records_the_supplied_registration_timestamp()
    {
        var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

        var project = Project.Register(Guid.NewGuid(), "Name", @"C:\repos\Foo", now);

        Assert.Equal(now, project.RegisteredAtUtc);
    }
}
