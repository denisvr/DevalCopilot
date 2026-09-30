using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The captured Git diff of a real disposable worktree (staged and unstaged tracked changes, a large
/// first file, a later small change, non-ASCII quoted paths, a space in a path, a missing final newline, a
/// binary file, a mode-only change, and an untracked file) run through the unchanged reader, the shared parser
/// and selector, and a real manifest builder.</summary>
public sealed class TrackedDiffRealGitTests : IDisposable
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-tracked-diff-{Guid.NewGuid():N}");
    private readonly GitWorkspaceEvidenceReader _reader = new(new ChildProcessExecutionAdapter());

    public TrackedDiffRealGitTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_real_worktree_diff_shows_the_prefix_defect_and_the_selected_hunks_fix_it()
    {
        var repository = CreateRepository();

        var plain = await _reader.CaptureAsync(repository, CancellationToken.None);
        var previews = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);
        var raw = Git(repository, "diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--binary", "HEAD");

        // The captured snapshot is unchanged: identical input gives the identical fingerprint and diff text.
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, plain.Outcome);
        Assert.Equal(plain.FingerprintSha256, previews.FingerprintSha256);
        Assert.Equal(plain.CompleteDiff, previews.CompleteDiff);
        Assert.Equal(raw, plain.CompleteDiff);
        var diff = plain.CompleteDiff!;

        // The defect: the first 8,192 characters end inside the large first file, so every later change is absent.
        var oldPrefix = diff[..8192];
        Assert.Contains("+late change", diff, StringComparison.Ordinal);
        Assert.DoesNotContain("late.txt", oldPrefix, StringComparison.Ordinal);
        Assert.DoesNotContain("+late change", oldPrefix, StringComparison.Ordinal);

        var parsed = TrackedDiffParser.Parse(diff);
        Assert.True(parsed.Recognized);
        Assert.DoesNotContain(parsed.Files, file => file.Kind == TrackedDiffFileKind.Unsupported);
        var kinds = parsed.Files.ToDictionary(file => file.Path!, file => file.Kind);
        Assert.Equal(TrackedDiffFileKind.Binary, kinds["bin.dat"]);
        Assert.Equal(TrackedDiffFileKind.MetadataOnly, kinds["run.sh"]);
        Assert.Equal(TrackedDiffFileKind.Text, kinds["café.txt"]);
        Assert.Equal(TrackedDiffFileKind.Text, kinds["with space.txt"]);
        Assert.Equal(TrackedDiffFileKind.Text, kinds["nonl.txt"]);

        var manifest = ClaudeCriticalReviewContextManifestBuilder.Build(
            Id, Id, Id, plain.FingerprintSha256!, "objective", Id, "summary", "{}", plain.ChangedPaths, diff,
            previews.UntrackedFiles);
        using var document = JsonDocument.Parse(manifest);
        var evidence = document.RootElement.GetProperty("changeEvidence");
        var selected = evidence.GetProperty("diff").GetString()!;

        Assert.True(Encoding.UTF8.GetByteCount(selected) <= ChangeEvidenceManifest.MaxInlinedDiffBytes);
        Assert.True(Encoding.UTF8.GetByteCount(manifest) <= ChangeEvidenceManifest.ManifestCeilingBytes);
        Assert.Contains("+late change", selected, StringComparison.Ordinal);
        Assert.Contains("+cafe changed", selected, StringComparison.Ordinal);
        Assert.Contains("+spaced changed", selected, StringComparison.Ordinal);
        Assert.Contains("\\ No newline at end of file", selected, StringComparison.Ordinal);
        Assert.Contains("old mode 100644", selected, StringComparison.Ordinal);
        Assert.Contains("\"a/caf\\303\\251.txt\"", selected, StringComparison.Ordinal);
        Assert.DoesNotContain("GIT binary patch", manifest, StringComparison.Ordinal);
        Assert.True(evidence.GetProperty("diffTruncated").GetBoolean());
        var items = evidence.GetProperty("diffSelection").GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("path").GetString()!, item => item);
        Assert.Equal("binary", items["bin.dat"].GetProperty("reason").GetString());
        Assert.Equal("hunk_too_large", items["big.txt"].GetProperty("reason").GetString());
        Assert.Equal("brand new untracked", evidence.GetProperty("untrackedFiles").GetProperty("files")[0].GetProperty("text").GetString());
        Assert.Equal(TrackedDiffParser.Parse(selected).Files.Count, selected.Split("diff --git ").Length - 1);
        Assert.DoesNotContain(TrackedDiffParser.Parse(selected).Files, file => file.Kind == TrackedDiffFileKind.Unsupported);

        // The oversized staged rewrite of big.txt contributes a separate, incomplete sample taken from the real diff.
        var samples = evidence.GetProperty("diffSelection").GetProperty("samples");
        Assert.False(samples.GetProperty("complete").GetBoolean());
        Assert.False(samples.GetProperty("patch").GetBoolean());
        Assert.Equal(1, samples.GetProperty("hunks").GetProperty("eligible").GetInt32());
        var sampled = samples.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("big.txt", sampled.GetProperty("path").GetString());
        Assert.Equal(1, sampled.GetProperty("hunk").GetInt32());
        Assert.Equal(600, sampled.GetProperty("changedLines").GetProperty("total").GetInt32());
        var sampledLines = sampled.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Equal("removed", sampledLines[0].GetProperty("side").GetString());
        Assert.StartsWith("original line 0001 ", sampledLines[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal(["removed", "removed", "removed", "removed", "added", "added", "added", "added"], sampledLines.Select(line => line.GetProperty("side").GetString()));
        Assert.StartsWith("rewritten line 0001 ", sampledLines[4].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.All(sampledLines, line => Assert.Contains(
            (line.GetProperty("side").GetString() == "added" ? "+" : "-") + line.GetProperty("text").GetString() + "\n",
            diff, StringComparison.Ordinal));
        Assert.DoesNotContain("rewritten line", selected, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(samples.GetRawText()) <= TrackedDiffSampler.MaxSectionBytes);
    }

    [Fact]
    public async Task A_second_capture_of_the_same_input_selects_byte_identical_evidence()
    {
        var repository = CreateRepository();

        var first = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);
        var second = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);
        string Build(GitWorkspaceEvidenceResult result) => ClaudeCriticalReviewContextManifestBuilder.Build(
            Id, Id, Id, result.FingerprintSha256!, "objective", Id, "summary", "{}", result.ChangedPaths, result.CompleteDiff,
            result.UntrackedFiles);

        Assert.Equal(first.FingerprintSha256, second.FingerprintSha256);
        Assert.Equal(Build(first), Build(second));
    }

    private string CreateRepository()
    {
        var path = Path.Combine(_root, "repository");
        Directory.CreateDirectory(path);
        Git(path, "init", "-q");
        Git(path, "config", "user.email", "test@example.com");
        Git(path, "config", "user.name", "Test");
        Git(path, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(path, "big.txt"), Lines(1, 700, "original"));
        File.WriteAllText(Path.Combine(path, "late.txt"), "one\ntwo\nthree\nfour\nfive\n");
        File.WriteAllText(Path.Combine(path, "café.txt"), "cafe\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path, "with space.txt"), "spaced\n");
        File.WriteAllBytes(Path.Combine(path, "nonl.txt"), "a\nb"u8.ToArray());
        File.WriteAllBytes(Path.Combine(path, "bin.dat"), [0, 1, 2, 3, 0, 255, 254, 0]);
        File.WriteAllText(Path.Combine(path, "run.sh"), "#!/bin/sh\n");
        Git(path, "add", "-A");
        Git(path, "commit", "-q", "-m", "initial");

        // Staged: a large rewrite of the first file, a mode-only change. Unstaged: everything else.
        File.WriteAllText(Path.Combine(path, "big.txt"), Lines(1, 300, "rewritten") + Lines(301, 700, "original"));
        Git(path, "add", "big.txt");
        Git(path, "update-index", "--chmod=+x", "run.sh");
        File.WriteAllText(Path.Combine(path, "late.txt"), "one\ntwo\n+late change\nfour\nfive\n".Replace("+late change", "late change"));
        File.WriteAllText(Path.Combine(path, "late.txt"), "one\ntwo\nlate change\nfour\nfive\n");
        File.WriteAllText(Path.Combine(path, "café.txt"), "cafe changed\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path, "with space.txt"), "spaced changed\n");
        File.WriteAllBytes(Path.Combine(path, "nonl.txt"), "a\nc"u8.ToArray());
        File.WriteAllBytes(Path.Combine(path, "bin.dat"), [0, 9, 8, 7, 0, 255, 250, 0, 1]);
        File.WriteAllText(Path.Combine(path, "new.txt"), "brand new untracked");
        return path;
    }

    private static string Lines(int from, int to, string label) =>
        string.Concat(Enumerable.Range(from, to - from + 1).Select(i => $"{label} line {i:D4} {new string('x', 40)}\n"));

    private static string Git(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return output;
    }
}
