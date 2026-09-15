using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

public sealed class GitCheckpointTests
{
    [Fact]
    public void Capture_requires_full_sha_identifiers_and_retains_changed_file_facts()
    {
        var checkpointId = Guid.NewGuid();
        var changedFile = GitChangedFile.Observe(Guid.NewGuid(), checkpointId, "src/App.tsx", null, "M", " ");

        var checkpoint = GitCheckpoint.Capture(
            checkpointId,
            Guid.NewGuid(),
            1,
            DateTimeOffset.UtcNow,
            new string('a', 40),
            new string('b', 64),
            [changedFile]);

        Assert.Equal(1, checkpoint.CheckpointNumber);
        Assert.Single(checkpoint.ChangedFiles);
        Assert.Throws<ArgumentException>(() => GitCheckpoint.Capture(
            Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow, "HEAD", new string('b', 64), []));
    }

    [Fact]
    public void Observe_requires_exactly_one_character_for_each_git_status_column()
    {
        Assert.Throws<ArgumentException>(() => GitChangedFile.Observe(
            Guid.NewGuid(), Guid.NewGuid(), "file.txt", null, "MM", " "));
    }
}
