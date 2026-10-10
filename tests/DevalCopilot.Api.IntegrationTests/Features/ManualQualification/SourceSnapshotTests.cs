using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>The source proof against a real Git repository: the fictitious fixture itself, and each kind of change the proof must see.</summary>
public sealed class SourceSnapshotTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"devalcopilot-xunit-source-{Guid.NewGuid():N}");
    private readonly SourceSnapshot _baseline;

    public SourceSnapshotTests()
    {
        Directory.CreateDirectory(_path);
        FixtureRepository.Create(_path);
        _baseline = GitSourceReader.Read(_path);
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_path, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Git(params string[] arguments) => GitProcess.Run(_path, arguments);

    [Fact]
    public void The_fixture_is_one_commit_on_main_with_four_tracked_files()
    {
        Assert.Equal("refs/heads/main", _baseline.HeadReference);
        Assert.Equal(40, _baseline.Head.Length);
        Assert.Equal(4, _baseline.IndexEntries.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Single(_baseline.References.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Reading_again_even_after_status_refreshed_the_index_is_the_same_proof()
    {
        Git("status", "--porcelain=v1");
        File.SetLastWriteTimeUtc(Path.Combine(_path, "README.md"), DateTime.UtcNow.AddMinutes(5));
        Git("status", "--porcelain=v1");

        var again = GitSourceReader.Read(_path);

        Assert.Empty(again.DifferencesFrom(_baseline));
    }

    [Fact]
    public void A_changed_tracked_file_is_seen_as_changed_bytes()
    {
        File.AppendAllText(Path.Combine(_path, "src", "Feature.cs"), "// edit\n");

        Assert.Equal(["Files"], GitSourceReader.Read(_path).DifferencesFrom(_baseline));
    }

    [Fact]
    public void A_new_untracked_hidden_file_is_seen()
    {
        var path = Path.Combine(_path, ".hidden-note");
        File.WriteAllText(path, "x");
        File.SetAttributes(path, FileAttributes.Hidden);

        Assert.Equal(["Files"], GitSourceReader.Read(_path).DifferencesFrom(_baseline));
    }

    [Fact]
    public void Staging_a_change_is_seen_in_the_index_and_the_bytes()
    {
        File.AppendAllText(Path.Combine(_path, "README.md"), "more\n");
        Git("add", "README.md");

        var differences = GitSourceReader.Read(_path).DifferencesFrom(_baseline);

        Assert.Contains("Index", differences);
        Assert.Contains("Files", differences);
    }

    [Fact]
    public void A_new_commit_moves_the_head()
    {
        Git("-c", "user.name=T", "-c", "user.email=t@example.invalid", "-c", "commit.gpgsign=false", "commit", "--allow-empty", "--no-verify", "-m", "x");

        Assert.Contains("Head", GitSourceReader.Read(_path).DifferencesFrom(_baseline));
    }

    [Fact]
    public void A_new_branch_is_a_reference_change_unless_it_is_the_one_the_host_was_asked_for()
    {
        Git("branch", "devalcopilot/workspace-1");

        var after = GitSourceReader.Read(_path);

        Assert.Equal(["References"], after.DifferencesFrom(_baseline));
        Assert.Empty(after.DifferencesFrom(_baseline, "refs/heads/devalcopilot/workspace-1"));
        Assert.Equal(["References"], after.DifferencesFrom(_baseline, "refs/heads/devalcopilot/other"));
    }

    [Fact]
    public void A_moved_or_removed_reference_is_never_tolerated()
    {
        Git("branch", "extra");
        var withExtra = GitSourceReader.Read(_path);
        Git("branch", "-D", "extra");

        Assert.Equal(["References"], GitSourceReader.Read(_path).DifferencesFrom(withExtra, "refs/heads/extra"));
    }

    [Fact]
    public void A_switched_head_is_seen()
    {
        Git("switch", "-c", "elsewhere");

        var differences = GitSourceReader.Read(_path).DifferencesFrom(_baseline);

        Assert.Contains("HeadReference", differences);
    }

    [Fact]
    public void A_linked_worktree_is_read_as_its_own_directory_and_shares_the_references()
    {
        var worktree = Path.Combine(Path.GetTempPath(), $"devalcopilot-xunit-worktree-{Guid.NewGuid():N}");
        try
        {
            Git("worktree", "add", "-b", "devalcopilot/ws", worktree);
            var linked = GitSourceReader.Read(worktree);

            Assert.Equal("refs/heads/devalcopilot/ws", linked.HeadReference);
            Assert.Equal(_baseline.Head, linked.Head);
            Assert.Equal(4, linked.IndexEntries.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            File.AppendAllText(Path.Combine(worktree, "README.md"), "edit\n");
            Assert.Equal(["Files"], GitSourceReader.Read(worktree).DifferencesFrom(linked));
        }
        finally
        {
            Git("worktree", "remove", "--force", worktree);
        }
    }
}
