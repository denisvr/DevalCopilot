using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>The cleanup authority of <see cref="LocalCommitStorage"/>: it removes a recorded artifact (current or legacy layout) or a
/// leaf the caller created, and nothing outside the owned root, no ancestor and no sibling.</summary>
public sealed class LocalCommitStorageOwnershipTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), $"devalcopilot-storage-ownership-{Guid.NewGuid():N}");
    private readonly LocalCommitStorage _storage;

    public LocalCommitStorageOwnershipTests()
    {
        Directory.CreateDirectory(_parent);
        _storage = new LocalCommitStorage(Path.Combine(_parent, "local-commit"));
    }

    public void Dispose() => Directory.Delete(_parent, recursive: true);

    private string Make(params string[] segments)
    {
        var directory = Path.Combine([_storage.Root, .. segments]);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Put(string directory, string name = "prepared.index")
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, name);
        return path;
    }

    [Fact]
    public void A_current_layout_artifact_is_removed_with_its_own_empty_leaf_and_leaves_every_sibling()
    {
        var preparation = Guid.NewGuid();
        var other = Guid.NewGuid();
        var leaf = Make("operations", preparation.ToString("N"));
        var file = Put(leaf);
        var sibling = Put(Make("operations", other.ToString("N")));

        Assert.True(_storage.TryRemoveRecordedArtifact(_storage.PreparedIndexRelativePath(preparation)));

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(leaf));
        Assert.True(File.Exists(sibling));
    }

    [Fact]
    public void A_legacy_operation_named_artifact_is_removed_without_reaching_other_preparations()
    {
        var operation = Guid.NewGuid();
        var legacy = Make("operations", operation.ToString("N"));
        var file = Put(legacy);
        var sibling = Put(Make("operations", Guid.NewGuid().ToString("N")));

        Assert.True(_storage.TryRemoveRecordedArtifact(_storage.LegacyPreparedIndexRelativePath(operation)));

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(legacy));
        Assert.True(File.Exists(sibling));
    }

    [Fact]
    public void A_directory_that_holds_anything_else_is_never_removed_recursively()
    {
        var operation = Guid.NewGuid();
        var leaf = Make("operations", operation.ToString("N"));
        var file = Put(leaf);
        var extra = Put(leaf, "belongs-to-someone-else.bin");

        Assert.True(_storage.TryRemoveRecordedArtifact(_storage.LegacyPreparedIndexRelativePath(operation)));

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(extra));
        Assert.True(Directory.Exists(leaf));
    }

    [Theory]
    [InlineData(@"..\outside\prepared.index")]
    [InlineData(@"..\..\prepared.index")]
    [InlineData(@"operations\prepared.index")]
    [InlineData(@"operations\a\b\prepared.index")]
    [InlineData(@"work\a\prepared.index")]
    [InlineData(@"hooks-empty\prepared.index")]
    [InlineData(@"sub\operations\a\prepared.index")]
    [InlineData(@"prepared.index")]
    [InlineData(@"C:\Windows\Temp\x\prepared.index")]
    public void A_recorded_path_outside_a_leaf_of_the_operations_folder_removes_nothing(string recorded)
    {
        var outside = Make("..", "outside");
        var sentinels = new[]
        {
            Put(outside),
            Put(Make("operations")),
            Put(Make("operations", "a", "b")),
            Put(Make("work", "a")),
            Put(Make("hooks-empty")),
            Put(Make("sub", "operations", "a")),
            Put(_storage.Root),
        };

        Assert.False(_storage.TryRemoveRecordedArtifact(recorded));

        Assert.All(sentinels, sentinel => Assert.True(File.Exists(sentinel), sentinel));
    }

    [Fact]
    public void An_owned_leaf_is_removed_with_its_contents_and_nothing_next_to_it()
    {
        var leaf = Make("work", "mine", "controlled-observation");
        Put(leaf, "index");
        var sibling = Put(Make("work", "theirs"));

        Assert.True(_storage.TryDeleteOwnedLeaf(Path.Combine(_storage.Root, "work", "mine")));

        Assert.False(Directory.Exists(Path.Combine(_storage.Root, "work", "mine")));
        Assert.True(File.Exists(sibling));
    }

    [Theory]
    [InlineData("")]
    [InlineData("work")]
    [InlineData("operations")]
    [InlineData(@"work\a\b")]
    [InlineData(@"hooks-empty")]
    [InlineData(@"..\outside")]
    public void Only_a_direct_child_of_the_work_or_operations_folder_is_an_owned_leaf(string relative)
    {
        var outside = Make("..", "outside");
        var sentinels = new[] { Put(outside), Put(Make("work", "a", "b")), Put(Make("operations", "x")), Put(Make("hooks-empty")), Put(_storage.Root, "root.txt") };

        Assert.False(_storage.TryDeleteOwnedLeaf(Path.Combine(_storage.Root, relative)));

        Assert.All(sentinels, sentinel => Assert.True(File.Exists(sentinel), sentinel));
    }

    [Fact]
    public void Each_preparation_identifier_names_distinct_scratch_and_artifact_leaves_independent_of_any_operation()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Assert.NotEqual(_storage.ScratchDirectory(first), _storage.ScratchDirectory(second));
        Assert.NotEqual(_storage.ArtifactDirectory(first), _storage.ArtifactDirectory(second));
        Assert.NotEqual(_storage.PreparedIndexRelativePath(first), _storage.PreparedIndexRelativePath(second));
        Assert.Equal(
            _storage.ArtifactDirectory(first),
            Path.GetDirectoryName(_storage.ResolveArtifact(_storage.PreparedIndexRelativePath(first))));
        Assert.NotEqual(_storage.ScratchDirectory(first), _storage.ArtifactDirectory(first));
    }
}
