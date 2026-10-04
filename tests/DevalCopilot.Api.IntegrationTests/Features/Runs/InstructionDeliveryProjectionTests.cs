using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.RealAdapterInstructionProbe;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// The two root instruction names are reserved to the controlled instruction section (ADR-0021). Real Git, real Windows hard
/// links, the real capture reader, real manifest builders, the real artifact store and the REAL critical-review adapter: for every
/// unsafe or inadmissible root source, the whole returned delivery evidence, the whole sealed manifest and the adapter's actual
/// standard input never carry the source's bytes outside the section, even when the section omits them, and unrelated change
/// evidence is untouched. The assertions deliberately inspect everything, not only <see cref="GitWorkspaceInstructionContext"/>.
/// </summary>
public sealed class InstructionDeliveryProjectionTests : IDisposable
{
    private const string Sentinel = "OUTSIDE-SENTINEL-7f3a: bytes from outside the worktree.";
    private const string Marker = "ROOTFILE-MARKER-91c2";
    private static readonly Guid Id = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-delivery-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore _store;
    private readonly string _launch;
    private readonly GitWorkspaceEvidenceReader _reader = new(new ChildProcessExecutionAdapter());

    public InstructionDeliveryProjectionTests()
    {
        Directory.CreateDirectory(_root);
        _store = new FilesystemArtifactStore(Path.Combine(_root, "artifacts"));
        _launch = Path.Combine(_root, "claude.exe");
        File.WriteAllText(_launch, string.Empty);
    }

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

    public static TheoryData<string, string> UnsafeSources
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var name in new[] { "AGENTS.md", "CLAUDE.md" })
            {
                foreach (var scenario in new[] { "clean-tracked-link", "dirty-tracked-link", "untracked-link" })
                {
                    data.Add(name, scenario);
                }
            }

            return data;
        }
    }

    public static TheoryData<string, string> InadmissibleSafeSources
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var name in new[] { "AGENTS.md", "CLAUDE.md" })
            {
                foreach (var scenario in new[] { "too-large-untracked", "too-large-dirty-tracked", "binary-untracked", "ignored", "manifest-budget" })
                {
                    data.Add(name, scenario);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(UnsafeSources))]
    public async Task An_unsafe_root_source_never_reaches_any_delivery_surface_outside_the_section(string name, string scenario)
    {
        var repository = CreateRepository();
        var outside = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outside, Sentinel);
        switch (scenario)
        {
            case "clean-tracked-link":
                HardLink(Path.Combine(repository, name), outside);
                Git(repository, "add", name);
                Git(repository, "commit", "-q", "-m", "link");
                break;
            case "dirty-tracked-link":
                CommitFile(repository, name, "a safe baseline");
                File.Delete(Path.Combine(repository, name));
                HardLink(Path.Combine(repository, name), outside);
                break;
            default:
                HardLink(Path.Combine(repository, name), outside);
                break;
        }

        var delivery = await DeliverAsync(repository, padding: 100);

        AssertNothingEscapes(delivery, Sentinel, expectedInSection: 0);
        Assert.Equal("Omitted", SourceOf(delivery.Manifest, name).GetProperty("status").GetString());
        Assert.Equal("containment_unproven", SourceOf(delivery.Manifest, name).GetProperty("reason").GetString());
        if (scenario != "clean-tracked-link")
        {
            AssertAccounted(delivery, name);
        }
    }

    [Theory]
    [MemberData(nameof(InadmissibleSafeSources))]
    public async Task A_safe_root_source_the_section_omits_is_not_delivered_generically_either(string name, string scenario)
    {
        var repository = CreateRepository();
        var padding = 100;
        var content = Marker + " " + new string('z', 20000);
        switch (scenario)
        {
            case "too-large-untracked":
                Write(repository, name, content);
                break;
            case "too-large-dirty-tracked":
                CommitFile(repository, name, "baseline");
                Write(repository, name, content);
                break;
            case "binary-untracked":
                File.WriteAllBytes(Path.Combine(repository, name), Encoding.UTF8.GetBytes(Marker + "\0 binary"));
                break;
            case "ignored":
                Write(repository, ".gitignore", name + "\n");
                CommitFile(repository, ".gitignore", name + "\n");
                Write(repository, name, Marker);
                break;
            default:
                // A complete, safe, dirty tracked source whose text the whole-manifest fit omits for budget.
                CommitFile(repository, name, "baseline");
                Write(repository, name, Marker + " " + new string('y', 5000));
                padding = -1;
                break;
        }

        var delivery = await DeliverAsync(repository, padding);

        AssertNothingEscapes(delivery, Marker, expectedInSection: 0);
        if (scenario == "manifest-budget")
        {
            Assert.Equal("manifest_budget", SourceOf(delivery.Manifest, name).GetProperty("reason").GetString());
        }

        if (scenario != "ignored")
        {
            AssertAccounted(delivery, name);
        }
    }

    public static TheoryData<string, string, bool> UnsupportedDiffFormats
    {
        get
        {
            var data = new TheoryData<string, string, bool>();
            foreach (var name in new[] { "AGENTS.md", "CLAUDE.md" })
            {
                foreach (var setting in new[] { "diff.noprefix", "diff.mnemonicprefix" })
                {
                    data.Add(name, setting, false);
                    data.Add(name, setting, true);
                }
            }

            return data;
        }
    }

    /// <summary>A repository configured with a diff header format this host does not decode returns, from the ordinary Git observation,
    /// a diff whose blocks cannot be attributed to a path with certainty. Unknown never means not reserved: when a tracked root
    /// instruction file changed, the whole generic diff is withheld, with fixed truthful accounting, and nothing else is disturbed.</summary>
    [Theory]
    [MemberData(nameof(UnsupportedDiffFormats))]
    public async Task An_undecodable_diff_header_never_carries_a_changed_root_source_outside_the_section(string name, string setting, bool withUnrelatedChange)
    {
        var repository = CreateRepository();
        var outside = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outside, Sentinel);
        Git(repository, "config", setting, "true");
        CommitFile(repository, name, "a safe baseline");
        File.Delete(Path.Combine(repository, name));
        HardLink(Path.Combine(repository, name), outside);
        if (withUnrelatedChange)
        {
            CommitFile(repository, "src/other.txt", "other baseline");
            Write(repository, "src/other.txt", "UNRELATED-DIFF-LINE changed");
            Write(repository, "notes/new.txt", "UNRELATED-UNTRACKED-TEXT");
        }

        // Precondition: the ordinary observation really carries the outside text, in a header format that is not the default.
        var plain = await _reader.CaptureAsync(repository, CancellationToken.None);
        Assert.Contains(Sentinel, plain.CompleteDiff, StringComparison.Ordinal);
        Assert.DoesNotContain("diff --git a/", plain.CompleteDiff, StringComparison.Ordinal);

        var delivery = await DeliverAsync(repository, padding: 100);

        AssertNothingEscapes(delivery, Sentinel, expectedInSection: 0);
        Assert.Equal("Omitted", SourceOf(delivery.Manifest, name).GetProperty("status").GetString());
        Assert.Equal("containment_unproven", SourceOf(delivery.Manifest, name).GetProperty("reason").GetString());
        AssertAccounted(delivery, name);
        // The whole generic diff is withheld, truthfully: no diff text at all, never complete, with a fixed reason.
        Assert.Null(delivery.Result.CompleteDiff);
        using var document = JsonDocument.Parse(delivery.Manifest);
        var evidence = document.RootElement.GetProperty("changeEvidence");
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("diff").ValueKind);
        Assert.True(evidence.GetProperty("diffTruncated").GetBoolean());
        var selection = evidence.GetProperty("diffSelection");
        Assert.False(selection.GetProperty("complete").GetBoolean());
        Assert.Equal("reserved_instruction_diff_withheld", selection.GetProperty("reason").GetString());
        Assert.Equal([name], selection.GetProperty("reservedInstructionFiles").EnumerateArray().Select(item => item.GetString()));
        if (withUnrelatedChange)
        {
            // Unrelated change evidence the uncertainty also withheld is still named, and unrelated untracked evidence is delivered.
            Assert.DoesNotContain("UNRELATED-DIFF-LINE", delivery.Manifest, StringComparison.Ordinal);
            Assert.Contains(delivery.Result.ChangedPaths, path => path.Path == "src/other.txt");
            Assert.Contains("UNRELATED-UNTRACKED-TEXT", delivery.Manifest, StringComparison.Ordinal);
            Assert.Contains("UNRELATED-UNTRACKED-TEXT", delivery.Stdin, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("diff.noprefix")]
    [InlineData("diff.mnemonicprefix")]
    public async Task An_undecodable_diff_is_delivered_unchanged_when_no_root_instruction_file_changed_and_the_ordinary_observation_is_untouched(string setting)
    {
        var repository = CreateRepository();
        Git(repository, "config", setting, "true");
        CommitFile(repository, "src/other.txt", "other baseline");
        Write(repository, "src/other.txt", "UNRELATED-DIFF-LINE changed");

        var plain = await _reader.CaptureAsync(repository, CancellationToken.None);
        var forAgent = await _reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        // Same raw observation, same fingerprint; the projection has nothing reserved to cut.
        Assert.Equal(plain.CompleteDiff, forAgent.CompleteDiff);
        Assert.Equal(plain.FingerprintSha256, forAgent.FingerprintSha256);
        Assert.Contains("UNRELATED-DIFF-LINE", forAgent.CompleteDiff, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("diff.noprefix")]
    [InlineData("diff.mnemonicprefix")]
    public async Task The_ordinary_capture_and_fingerprint_are_not_normalized_by_the_delivery_projection(string setting)
    {
        var repository = CreateRepository();
        var outside = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outside, Sentinel);
        Git(repository, "config", setting, "true");
        CommitFile(repository, "AGENTS.md", "a safe baseline");
        File.Delete(Path.Combine(repository, "AGENTS.md"));
        HardLink(Path.Combine(repository, "AGENTS.md"), outside);

        var plain = await _reader.CaptureAsync(repository, CancellationToken.None);
        var previews = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);
        var forAgent = await _reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        // The raw observation is unchanged (it still carries the text; it is not claimed to have the containment guarantee) and the
        // fingerprint is the raw observation's, identical to what an ordinary capture records.
        Assert.Contains(Sentinel, plain.CompleteDiff, StringComparison.Ordinal);
        Assert.Contains(Sentinel, previews.CompleteDiff, StringComparison.Ordinal);
        Assert.Equal(plain.FingerprintSha256, forAgent.FingerprintSha256);
        Assert.Equal(plain.FingerprintSha256, previews.FingerprintSha256);
        Assert.Equal(plain.ChangedPaths, forAgent.ChangedPaths);
        Assert.Null(forAgent.CompleteDiff);
    }

    [Theory]
    [InlineData("AGENTS.md")]
    [InlineData("CLAUDE.md")]
    public async Task A_complete_root_source_appears_only_in_the_section_and_unrelated_evidence_is_preserved(string name)
    {
        var repository = CreateRepository();
        CommitFile(repository, name, "baseline conventions");
        Write(repository, name, Marker + " dirty tracked conventions");
        CommitFile(repository, "src/other.txt", "other baseline");
        Write(repository, "src/other.txt", "UNRELATED-DIFF-LINE changed");
        Write(repository, "notes/new.txt", "UNRELATED-UNTRACKED-TEXT");
        var second = name == "AGENTS.md" ? "CLAUDE.md" : "AGENTS.md";
        Write(repository, second, "SECOND-ROOT-UNTRACKED " + Marker);

        var delivery = await DeliverAsync(repository, padding: 100);

        AssertNothingEscapes(delivery, Marker, expectedInSection: 2);
        Assert.Contains("UNRELATED-DIFF-LINE", delivery.Manifest, StringComparison.Ordinal);
        Assert.Contains("UNRELATED-UNTRACKED-TEXT", delivery.Manifest, StringComparison.Ordinal);
        Assert.Contains("UNRELATED-DIFF-LINE", delivery.Stdin, StringComparison.Ordinal);
        Assert.Contains("UNRELATED-DIFF-LINE", delivery.Result.CompleteDiff, StringComparison.Ordinal);
        Assert.Contains(delivery.Result.UntrackedFiles!, file => file.Path == "notes/new.txt" && file.Text!.Contains("UNRELATED-UNTRACKED-TEXT", StringComparison.Ordinal));
        AssertAccounted(delivery, "AGENTS.md");
        AssertAccounted(delivery, "CLAUDE.md");
        Assert.Contains(delivery.Result.ChangedPaths, path => path.Path == name);
        using var document = JsonDocument.Parse(delivery.Manifest);
        var selection = document.RootElement.GetProperty("changeEvidence").GetProperty("diffSelection");
        Assert.False(selection.GetProperty("complete").GetBoolean());
        Assert.True(document.RootElement.GetProperty("changeEvidence").GetProperty("diffTruncated").GetBoolean());
        // Both reserved paths changed (one tracked, one untracked): both are named, in a fixed order.
        Assert.Equal(
            ["AGENTS.md", "CLAUDE.md"],
            selection.GetProperty("reservedInstructionFiles").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task Another_builder_with_the_same_capture_delivers_nothing_of_a_reserved_file_either()
    {
        var repository = CreateRepository();
        var outside = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outside, Sentinel);
        CommitFile(repository, "AGENTS.md", "a safe baseline");
        File.Delete(Path.Combine(repository, "AGENTS.md"));
        HardLink(Path.Combine(repository, "AGENTS.md"), outside);
        var result = await _reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        var manifest = ImplementationContextManifestBuilder.BuildForAcceptedOriginalProposal(
            Id, Id, Id, result.FingerprintSha256!, "objective", Id, "summary", "{}",
            new ImplementationContextManifestBuilder.AcceptanceEvidence("a", "{}"), result.ChangedPaths, result.CompleteDiff,
            [new ImplementationContextManifestBuilder.VerificationCommandReference("build", true)],
            ProjectInstructionContextManifest.Prepare(Id, Id, result.FingerprintSha256!, result.InstructionContext),
            result.UntrackedFiles);

        Assert.DoesNotContain("OUTSIDE-SENTINEL", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("OUTSIDE-SENTINEL", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_ordinary_capture_still_returns_the_raw_evidence_it_always_did()
    {
        var repository = CreateRepository();
        CommitFile(repository, "AGENTS.md", "baseline");
        Write(repository, "AGENTS.md", Marker + " raw dirty");
        Write(repository, "CLAUDE.md", Marker + " raw untracked");

        var plain = await _reader.CaptureAsync(repository, CancellationToken.None);
        var previews = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);
        var forAgent = await _reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        Assert.Contains(Marker, plain.CompleteDiff, StringComparison.Ordinal);
        Assert.Contains(Marker, previews.CompleteDiff, StringComparison.Ordinal);
        Assert.Contains(Marker, previews.UntrackedFiles!.Single().Text, StringComparison.Ordinal);
        // The checkpoint fingerprint is the raw observation's and never depends on the delivery projection.
        Assert.Equal(plain.FingerprintSha256, forAgent.FingerprintSha256);
        Assert.Equal(plain.ChangedPaths, forAgent.ChangedPaths);
        Assert.DoesNotContain(Marker, forAgent.CompleteDiff ?? string.Empty, StringComparison.Ordinal);
    }

    // ---- delivery -------------------------------------------------------------------------------------------------------

    private sealed record Delivery(GitWorkspaceEvidenceResult Result, string Manifest, string Stdin);

    private static string BuildManifest(GitWorkspaceEvidenceResult result, int padding) =>
        ClaudeCriticalReviewContextManifestBuilder.Build(
            Id, Id, Id, result.FingerprintSha256!, "objective", Id, "summary", JsonSerializer.Serialize(new { p = new string('p', padding) }),
            result.ChangedPaths, result.CompleteDiff,
            ProjectInstructionContextManifest.Prepare(Id, Id, result.FingerprintSha256!, result.InstructionContext), result.UntrackedFiles);

    /// <summary>The largest padding whose manifest still fits the 32 KiB ceiling only after the section's text was omitted for budget.</summary>
    private static int PaddingThatOmitsTheTextForBudget(GitWorkspaceEvidenceResult result)
    {
        for (var padding = 22000; padding < 33000; padding += 50)
        {
            var manifest = BuildManifest(result, padding);
            if (Encoding.UTF8.GetByteCount(manifest) > 32 * 1024)
            {
                break;
            }

            if (manifest.Contains("manifest_budget", StringComparison.Ordinal))
            {
                return padding;
            }
        }

        throw new InvalidOperationException("No padding omits the instruction text for budget while the manifest fits.");
    }

    private async Task<Delivery> DeliverAsync(string repository, int padding)
    {
        var result = await _reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        if (padding < 0)
        {
            padding = PaddingThatOmitsTheTextForBudget(result);
        }

        var manifest = BuildManifest(result, padding);
        var attempt = Guid.NewGuid();
        var run = Guid.NewGuid();
        var partial = _store.GetPartialPath(run, attempt, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
        await File.WriteAllTextAsync(partial, manifest);
        var sealedFile = await _store.SealAsync(run, attempt, ArtifactPurpose.AgentContextManifest, CancellationToken.None);
        Assert.NotNull(sealedFile);

        var process = new CapturingProcessDouble();
        await new ClaudeCriticalReviewAdapter(process, _store).InvokeAsync(
            new CriticalReviewInvocationRequest(
                run, attempt, repository, sealedFile!.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, _launch,
                TimeSpan.FromMinutes(5), 256 * 1024, 512 * 1024),
            CancellationToken.None);
        var stdin = Encoding.UTF8.GetString(Assert.Single(process.Requests).StandardInput!);
        Assert.Equal(manifest, stdin);
        return new Delivery(result, manifest, stdin);
    }

    private static JsonElement SourceOf(string manifest, string name)
    {
        using var document = JsonDocument.Parse(manifest);
        return document.RootElement.GetProperty("projectInstructionContext").GetProperty("sources").EnumerateArray()
            .Single(source => source.GetProperty("fileName").GetString() == name).Clone();
    }

    /// <summary>The marker appears nowhere in the returned evidence, and in the sealed manifest and the adapter's stdin only as the
    /// controlled section's own Complete text, exactly <paramref name="expectedInSection"/> times.</summary>
    private static void AssertNothingEscapes(Delivery delivery, string marker, int expectedInSection)
    {
        var escaped = JsonSerializer.Serialize(marker)[1..^1];
        // Everything the capture returned except the controlled section itself (which may legitimately hold a Complete text).
        Assert.DoesNotContain(marker, JsonSerializer.Serialize(delivery.Result with { InstructionContext = null }), StringComparison.Ordinal);
        Assert.DoesNotContain(marker, delivery.Result.CompleteDiff ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(delivery.Result.UntrackedFiles ?? [], file => (file.Text ?? string.Empty).Contains(marker, StringComparison.Ordinal));
        Assert.Equal(expectedInSection, delivery.Manifest.Split(escaped).Length - 1);
        Assert.Equal(expectedInSection, delivery.Stdin.Split(escaped).Length - 1);
        using var document = JsonDocument.Parse(delivery.Manifest);
        foreach (var property in document.RootElement.EnumerateObject().Where(property => property.Name != "projectInstructionContext"))
        {
            Assert.DoesNotContain(marker, property.Value.GetRawText(), StringComparison.Ordinal);
        }
    }

    /// <summary>The path stays in the changed paths and the suppression is stated, never presented as complete evidence.</summary>
    private static void AssertAccounted(Delivery delivery, string name)
    {
        Assert.Contains(delivery.Result.ChangedPaths, path => path.Path == name);
        using var document = JsonDocument.Parse(delivery.Manifest);
        var evidence = document.RootElement.GetProperty("changeEvidence");
        Assert.Contains(evidence.GetProperty("changedPaths").EnumerateArray(), path => path.GetProperty("Path").GetString() == name);
        var untracked = evidence.TryGetProperty("untrackedFiles", out var section)
            && section.TryGetProperty("files", out var files)
            && files.ValueKind == JsonValueKind.Array
            && files.EnumerateArray().Any(file => file.GetProperty("path").GetString() == name
                && file.GetProperty("omissionReason").GetString() == "reserved_instruction_file"
                && !file.GetProperty("contentComplete").GetBoolean());
        var tracked = evidence.TryGetProperty("diffSelection", out var selection)
            && selection.TryGetProperty("reservedInstructionFiles", out var reserved)
            && reserved.EnumerateArray().Any(item => item.GetString() == name)
            && !selection.GetProperty("complete").GetBoolean();
        Assert.True(untracked || tracked, $"{name} has no fixed reserved_instruction_file accounting in the manifest.");
    }

    // ---- real repository -----------------------------------------------------------------------------------------------

    private string CreateRepository()
    {
        var path = Path.Combine(_root, "repo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        Git(path, "init", "-q");
        Git(path, "config", "user.email", "test@example.com");
        Git(path, "config", "user.name", "Test");
        Git(path, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(path, "tracked.txt"), "original");
        Git(path, "add", "tracked.txt");
        Git(path, "commit", "-q", "-m", "initial");
        return path;
    }

    private static void Write(string repository, string relativePath, string content)
    {
        var path = Path.Combine(repository, relativePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
    }

    private static void CommitFile(string repository, string relativePath, string content)
    {
        Write(repository, relativePath, content);
        Git(repository, "add", relativePath);
        Git(repository, "commit", "-q", "-m", "add " + relativePath);
    }

    private static void HardLink(string linkPath, string targetPath) => Run("cmd.exe", ["/c", "mklink", "/H", linkPath, targetPath], null);

    private static void Git(string workingDirectory, params string[] arguments) => Run("git", arguments, workingDirectory);

    private static void Run(string fileName, IEnumerable<string> arguments, string? workingDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
