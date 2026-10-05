using System.Runtime.Versioning;
using System.Text;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>R2 of the attested tracked-change review: the 512 KiB retained-source budget bounds what one observation actually keeps
/// alive, baseline cache included, not only the text it finally delivers. The oracle is the real production observer driven with its
/// own cache over real Git and real NTFS files: after every observation, the cache holds the text of admitted baselines only, so the
/// cache can never exceed the observation's accounted facts and never the limit. A path omitted after its baseline was read (no
/// content difference, invalid current bytes, too many lines) leaves nothing behind.</summary>
[Collection(EnvironmentPathMutationCollection.Name)]
[SupportedOSPlatform("windows")]
public sealed class GitWorkspaceTrackedRetentionTests : IDisposable
{
    private const int KiB = 1024;
    private const int Limit = GitWorkspaceTrackedFile.MaxRetainedBytes;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-retention-{Guid.NewGuid():N}");
    private readonly GitWorkspaceEvidenceReader _reader = new(new ChildProcessExecutionAdapter());

    public GitWorkspaceTrackedRetentionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    [WindowsOnlyFact]
    public async Task Four_distinct_omitted_baselines_leave_nothing_in_the_cache_and_the_capture_still_omits_each_of_them()
    {
        var repository = CreateRepository();
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt" })
        {
            Commit(repository, name, Text(name[0], 200 * KiB));
        }

        foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt" })
        {
            StageThenRestore(repository, name);
        }

        var cache = new Dictionary<string, GitWorkspaceEvidenceReader.BaselineRead>(StringComparer.Ordinal);
        var observation = await ObserveAsync(repository, cache);
        var captured = await _reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: false, CancellationToken.None);

        Assert.All(observation.Files!, file => Assert.Equal(GitWorkspaceTrackedOmission.NoContentDifference, file.Omission));
        Assert.Equal(4, observation.Files!.Count);
        Assert.Equal(0, CacheBytes(cache));
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, captured.Outcome);
        Assert.All(captured.TrackedFiles!, file => Assert.Equal(GitWorkspaceTrackedOmission.NoContentDifference, file.Omission));
    }

    [WindowsOnlyFact]
    public async Task Distinct_omitted_baselines_stay_unretained_across_both_observations_of_the_same_cache()
    {
        var repository = CreateRepository();
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt" })
        {
            Commit(repository, name, Text(name[0], 200 * KiB));
        }

        foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt" })
        {
            StageThenRestore(repository, name);
        }

        var cache = new Dictionary<string, GitWorkspaceEvidenceReader.BaselineRead>(StringComparer.Ordinal);
        var first = await ObserveAsync(repository, cache);
        var afterFirst = CacheBytes(cache);
        var second = await ObserveAsync(repository, cache);

        Assert.Equal(first.Files, second.Files);
        Assert.Equal(0, afterFirst);
        Assert.Equal(0, CacheBytes(cache));
    }

    [WindowsOnlyFact]
    public async Task A_baseline_whose_current_bytes_are_invalid_or_too_long_is_omitted_without_staying_cached()
    {
        var repository = CreateRepository();
        Commit(repository, "bad.txt", Text('p', 150 * KiB));
        File.WriteAllBytes(Path.Combine(repository, "bad.txt"), [0xC3, 0x28, 0x0A]);
        Commit(repository, "long.txt", Text('q', 150 * KiB));
        File.WriteAllText(Path.Combine(repository, "long.txt"), string.Concat(Enumerable.Repeat("\n", 8193)));

        var cache = new Dictionary<string, GitWorkspaceEvidenceReader.BaselineRead>(StringComparer.Ordinal);
        var observation = await ObserveAsync(repository, cache);

        Assert.Equal(GitWorkspaceTrackedOmission.InvalidUtf8, observation.Files![0].Omission);
        Assert.Equal(GitWorkspaceTrackedOmission.TooManyLines, observation.Files[1].Omission);
        Assert.Equal(0, CacheBytes(cache));
    }

    [WindowsOnlyFact]
    public async Task Only_the_admitted_files_baselines_stay_cached_beside_omitted_siblings_and_a_later_healthy_sibling()
    {
        var repository = CreateRepository();
        Commit(repository, "a-heavy.txt", Text('a', 100 * KiB));
        Commit(repository, "b-omitted.txt", Text('b', 150 * KiB));
        Commit(repository, "c-omitted.txt", Text('c', 150 * KiB));
        Commit(repository, "z-healthy.txt", Text('z', 50 * KiB));
        StageThenRestore(repository, "b-omitted.txt");
        StageThenRestore(repository, "c-omitted.txt");
        Write(repository, "a-heavy.txt", Text('A', 40 * KiB));
        Write(repository, "z-healthy.txt", Text('Z', 50 * KiB));

        var cache = new Dictionary<string, GitWorkspaceEvidenceReader.BaselineRead>(StringComparer.Ordinal);
        var first = await ObserveAsync(repository, cache);
        var afterFirst = CacheBytes(cache);
        var second = await ObserveAsync(repository, cache);

        Assert.Equal(new GitWorkspaceTrackedFile("a-heavy.txt", null, Text('a', 100 * KiB), Text('A', 40 * KiB)), first.Files![0]);
        Assert.Equal(GitWorkspaceTrackedOmission.NoContentDifference, first.Files[1].Omission);
        Assert.Equal(GitWorkspaceTrackedOmission.NoContentDifference, first.Files[2].Omission);
        Assert.Equal(new GitWorkspaceTrackedFile("z-healthy.txt", null, Text('z', 50 * KiB), Text('Z', 50 * KiB)), first.Files[3]);
        Assert.Equal(first.Files, second.Files);
        var admittedBaselines = 100 * KiB + 50 * KiB;
        Assert.Equal(admittedBaselines, afterFirst);
        Assert.Equal(admittedBaselines, CacheBytes(cache));
        Assert.True(CacheBytes(cache) <= FactBytes(second));
    }

    [WindowsOnlyFact]
    public async Task A_baseline_shared_by_admitted_files_is_cached_once_within_their_accounted_facts()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", Text('s', 150 * KiB));
        File.Copy(Path.Combine(repository, "a.txt"), Path.Combine(repository, "b.txt"));
        Git(repository, "add", "b.txt");
        Git(repository, "commit", "-q", "-m", "copy");
        Write(repository, "a.txt", Text('1', 10 * KiB));
        Write(repository, "b.txt", Text('2', 10 * KiB));

        var cache = new Dictionary<string, GitWorkspaceEvidenceReader.BaselineRead>(StringComparer.Ordinal);
        var first = await ObserveAsync(repository, cache);
        var second = await ObserveAsync(repository, cache);

        Assert.All(first.Files!, file => Assert.Null(file.Omission));
        Assert.Equal(first.Files, second.Files);
        Assert.Equal(150 * KiB, CacheBytes(cache));
        Assert.True(CacheBytes(cache) <= FactBytes(second));
    }

    [WindowsOnlyFact]
    public async Task A_shared_baseline_omitted_for_no_difference_is_not_cached_and_a_later_changed_sibling_is_still_attested()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", Text('s', 120 * KiB));
        File.Copy(Path.Combine(repository, "a.txt"), Path.Combine(repository, "b.txt"));
        Git(repository, "add", "b.txt");
        Git(repository, "commit", "-q", "-m", "copy");
        Write(repository, "a.txt", Text('t', 120 * KiB));
        Git(repository, "add", "a.txt");
        Write(repository, "a.txt", Text('s', 120 * KiB));
        Write(repository, "b.txt", Text('u', 20 * KiB));

        var cache = new Dictionary<string, GitWorkspaceEvidenceReader.BaselineRead>(StringComparer.Ordinal);
        var observation = await ObserveAsync(repository, cache);

        Assert.Equal(GitWorkspaceTrackedOmission.NoContentDifference, observation.Files![0].Omission);
        Assert.Equal(new GitWorkspaceTrackedFile("b.txt", null, Text('s', 120 * KiB), Text('u', 20 * KiB)), observation.Files[1]);
        Assert.Equal(120 * KiB, CacheBytes(cache));
        Assert.True(CacheBytes(cache) <= FactBytes(observation));
    }

    [WindowsOnlyFact]
    public async Task A_file_exactly_at_the_retained_limit_is_admitted_and_the_next_file_over_it_is_an_aggregate_omission_before_its_baseline_is_read()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", Text('a', Limit / 2));
        Commit(repository, "b.txt", Text('b', 100 * KiB));
        Write(repository, "a.txt", Text('A', Limit / 2));
        Write(repository, "b.txt", Text('B', 100 * KiB));

        var cache = new Dictionary<string, GitWorkspaceEvidenceReader.BaselineRead>(StringComparer.Ordinal);
        var observation = await ObserveAsync(repository, cache);

        Assert.Equal(Limit, FactBytes(observation));
        Assert.Equal(GitWorkspaceTrackedOmission.AggregateLimit, observation.Files![1].Omission);
        Assert.Equal(Limit / 2, CacheBytes(cache));
    }

    [WindowsOnlyFact]
    public async Task One_byte_over_the_retained_limit_omits_the_file_and_caches_nothing_for_it()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", Text('a', Limit / 2));
        Commit(repository, "b.txt", Text('b', 1));
        Write(repository, "a.txt", Text('A', Limit / 2 - 1));
        Write(repository, "b.txt", Text('B', 3));

        var cache = new Dictionary<string, GitWorkspaceEvidenceReader.BaselineRead>(StringComparer.Ordinal);
        var observation = await ObserveAsync(repository, cache);

        Assert.Null(observation.Files![0].Omission);
        Assert.Equal(GitWorkspaceTrackedOmission.AggregateLimit, observation.Files[1].Omission);
        Assert.Equal(Limit / 2, CacheBytes(cache));
        Assert.True(CacheBytes(cache) <= Limit);
    }

    private async Task<GitWorkspaceEvidenceReader.GitWorkspaceTrackedObservation> ObserveAsync(
        string repository, Dictionary<string, GitWorkspaceEvidenceReader.BaselineRead> cache)
    {
        var descriptor = HostCapabilityCatalog.Get(Capability.Git);
        var gitPath = HostExecutableResolver.TryResolve(descriptor.CandidateExecutableNames, descriptor.FallbackDirectories)!;
        var head = GitWorkspaceUntrackedPreviewTests.GitOutput(repository, "rev-parse", "HEAD").Trim();
        var changed = GitWorkspaceUntrackedPreviewTests.GitOutput(repository, "status", "--porcelain=v1", "--branch", "--no-renames", "--untracked-files=no")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith("##", StringComparison.Ordinal))
            .Select(line => new GitWorkspaceChangedPath(line[3..].TrimEnd('\r'), null, line[..1], line.Substring(1, 1)))
            .ToArray();
        var observation = await _reader.ObserveTrackedFilesAsync(gitPath, repository, head, changed, cache, reserveInstructionNames: true, CancellationToken.None);
        Assert.Null(observation.Failure);
        return observation;
    }

    private static long CacheBytes(Dictionary<string, GitWorkspaceEvidenceReader.BaselineRead> cache) =>
        cache.Values.Sum(read => read.Text is null ? 0L : Encoding.UTF8.GetByteCount(read.Text));

    private static long FactBytes(GitWorkspaceEvidenceReader.GitWorkspaceTrackedObservation observation) =>
        observation.Files!.Sum(file =>
            (file.BeforeText is null ? 0L : Encoding.UTF8.GetByteCount(file.BeforeText))
            + (file.AfterText is null ? 0L : Encoding.UTF8.GetByteCount(file.AfterText)));

    /// <summary>Real MM state, made after every commit (a later commit would take the staged change): the index gets different
    /// content and the working bytes are restored to exactly what HEAD holds.</summary>
    private static void StageThenRestore(string repository, string name)
    {
        var headBytes = File.ReadAllBytes(Path.Combine(repository, name));
        File.WriteAllBytes(Path.Combine(repository, name), [.. headBytes, .. "staged difference\n"u8.ToArray()]);
        Git(repository, "add", name);
        File.WriteAllBytes(Path.Combine(repository, name), headBytes);
    }

    /// <summary>Exactly <paramref name="bytes"/> UTF-8 bytes of ASCII lines of at most 128 bytes (terminator included).</summary>
    private static string Text(char fill, int bytes)
    {
        var text = new StringBuilder(bytes);
        var remaining = bytes;
        while (remaining > 0)
        {
            var line = Math.Min(128, remaining);
            text.Append(fill, line - 1).Append('\n');
            remaining -= line;
        }

        return text.ToString();
    }

    private string CreateRepository()
    {
        var path = Path.Combine(_root, "repository");
        Directory.CreateDirectory(path);
        Git(path, "init", "-q");
        Git(path, "config", "user.email", "test@example.com");
        Git(path, "config", "user.name", "Test");
        Git(path, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(path, "seed.txt"), "seed\n");
        Git(path, "add", "seed.txt");
        Git(path, "commit", "-q", "-m", "initial");
        return path;
    }

    private static void Write(string repository, string name, string content) =>
        File.WriteAllBytes(Path.Combine(repository, name), new UTF8Encoding(false).GetBytes(content));

    private static void Commit(string repository, string name, string content)
    {
        Write(repository, name, content);
        Git(repository, "add", name);
        Git(repository, "commit", "-q", "-m", "add " + name);
    }

    private static void Git(string workingDirectory, params string[] arguments) => GitWorkspaceUntrackedPreviewTests.RunGit(workingDirectory, arguments);
}
