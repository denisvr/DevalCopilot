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

    [Fact]
    public void Register_starts_physical_identity_unresolved_with_no_failure_reason()
    {
        var project = Project.Register(Guid.NewGuid(), "Name", @"C:\repos\Foo", DateTimeOffset.UtcNow);

        Assert.Equal(PhysicalIdentityStatus.Unresolved, project.PhysicalIdentityStatus);
        Assert.Equal(PhysicalIdentityFailureReason.None, project.PhysicalIdentityFailureReason);
        Assert.Null(project.PhysicalVolumeSerialNumber);
        Assert.Null(project.PhysicalFileId);
    }

    [Fact]
    public void ReserveWorkspaceNumber_returns_monotonically_increasing_values_starting_at_one()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\DevalCopilot", DateTimeOffset.UtcNow);

        var first = project.ReserveWorkspaceNumber();
        var second = project.ReserveWorkspaceNumber();

        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public void ReserveVerificationCommandNumber_returns_monotonically_increasing_values_starting_at_one()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\DevalCopilot", DateTimeOffset.UtcNow);

        var first = project.ReserveVerificationCommandNumber();
        var second = project.ReserveVerificationCommandNumber();

        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public void ReserveVerificationExecutionNumber_returns_monotonically_increasing_values_starting_at_one()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\DevalCopilot", DateTimeOffset.UtcNow);

        Assert.Equal(1, project.ReserveVerificationExecutionNumber());
        Assert.Equal(2, project.ReserveVerificationExecutionNumber());
    }

    [Fact]
    public void RecordPhysicalIdentityResolved_sets_resolved_status_and_clears_any_failure_reason()
    {
        var project = Project.Register(Guid.NewGuid(), "Name", @"C:\repos\Foo", DateTimeOffset.UtcNow);
        var fileId = new byte[16];
        fileId[0] = 7;

        project.RecordPhysicalIdentityResolved(42UL, fileId);

        Assert.Equal(PhysicalIdentityStatus.Resolved, project.PhysicalIdentityStatus);
        Assert.Equal(PhysicalIdentityFailureReason.None, project.PhysicalIdentityFailureReason);
        Assert.Equal(42UL, project.PhysicalVolumeSerialNumber);
        Assert.Equal(fileId, project.PhysicalFileId);
    }

    [Fact]
    public void RecordPhysicalIdentityResolved_is_idempotent_when_the_tuple_matches_the_already_resolved_one()
    {
        var project = Project.Register(Guid.NewGuid(), "Name", @"C:\repos\Foo", DateTimeOffset.UtcNow);
        var fileId = new byte[16];
        project.RecordPhysicalIdentityResolved(1UL, fileId);

        project.RecordPhysicalIdentityResolved(1UL, fileId);

        Assert.Equal(PhysicalIdentityStatus.Resolved, project.PhysicalIdentityStatus);
    }

    [Fact]
    public void RecordPhysicalIdentityResolved_throws_rather_than_silently_overwrite_a_different_resolved_tuple()
    {
        var project = Project.Register(Guid.NewGuid(), "Name", @"C:\repos\Foo", DateTimeOffset.UtcNow);
        project.RecordPhysicalIdentityResolved(1UL, new byte[16]);
        var differentFileId = new byte[16];
        differentFileId[0] = 9;

        Assert.Throws<InvalidOperationException>(() => project.RecordPhysicalIdentityResolved(2UL, differentFileId));
    }

    [Fact]
    public void RecordPhysicalIdentityUnavailable_sets_unavailable_status_with_the_given_reason()
    {
        var project = Project.Register(Guid.NewGuid(), "Name", @"C:\repos\Foo", DateTimeOffset.UtcNow);

        project.RecordPhysicalIdentityUnavailable(PhysicalIdentityFailureReason.PathInaccessible);

        Assert.Equal(PhysicalIdentityStatus.Unavailable, project.PhysicalIdentityStatus);
        Assert.Equal(PhysicalIdentityFailureReason.PathInaccessible, project.PhysicalIdentityFailureReason);
    }

    [Fact]
    public void RecordPhysicalIdentityUnavailable_rejects_a_none_reason()
    {
        var project = Project.Register(Guid.NewGuid(), "Name", @"C:\repos\Foo", DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => project.RecordPhysicalIdentityUnavailable(PhysicalIdentityFailureReason.None));
    }

    [Fact]
    public void RecordPhysicalIdentityUnavailable_never_demotes_an_already_resolved_identity()
    {
        var project = Project.Register(Guid.NewGuid(), "Name", @"C:\repos\Foo", DateTimeOffset.UtcNow);
        project.RecordPhysicalIdentityResolved(1UL, new byte[16]);

        Assert.Throws<InvalidOperationException>(
            () => project.RecordPhysicalIdentityUnavailable(PhysicalIdentityFailureReason.PathInaccessible));
        Assert.Equal(PhysicalIdentityStatus.Resolved, project.PhysicalIdentityStatus);
    }

    [Fact]
    public void PhysicalIdentityMatches_is_false_until_resolved_and_true_only_for_the_exact_tuple()
    {
        var project = Project.Register(Guid.NewGuid(), "Name", @"C:\repos\Foo", DateTimeOffset.UtcNow);
        var fileId = new byte[16];
        fileId[3] = 5;

        Assert.False(project.PhysicalIdentityMatches(1UL, fileId));

        project.RecordPhysicalIdentityResolved(1UL, fileId);

        Assert.True(project.PhysicalIdentityMatches(1UL, fileId));
        Assert.False(project.PhysicalIdentityMatches(2UL, fileId));
        Assert.False(project.PhysicalIdentityMatches(1UL, new byte[16]));
    }
}
