using System.Text.Json;
using System.Diagnostics;
using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>Real Git plus the real Windows filesystem: bounded, identity-verified previews of untracked
/// files. Each disposable workspace lives under a temporary root that is removed, junctions first, on
/// disposal. Cases that need NTFS junctions or file symbolic links are skipped, and reported as skipped,
/// where the host cannot create them.</summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class GitWorkspaceUntrackedPreviewTests : IDisposable
{
    private const string OutsideSecret = "TOP SECRET TEXT FROM OUTSIDE THE WORKTREE";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-untracked-{Guid.NewGuid():N}");
    private readonly List<string> _junctions = [];
    private readonly GitWorkspaceEvidenceReader _reader = new(new ChildProcessExecutionAdapter());

    public GitWorkspaceUntrackedPreviewTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (var junction in _junctions.Where(Directory.Exists))
        {
            Directory.Delete(junction, recursive: false);
        }

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
    public async Task Previews_expose_the_exact_untracked_text_that_the_plain_capture_omits_for_the_same_snapshot()
    {
        var repository = CreateRepository();
        File.WriteAllText(Path.Combine(repository, "tracked.txt"), "changed tracked content");
        Write(repository, "notes/one.txt", "first untracked file\nwith two lines\n");
        Write(repository, "two.txt", "second untracked file");

        var plain = await _reader.CaptureAsync(repository, CancellationToken.None);
        var previews = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);

        // Before this slice's preview: the untracked text is absent from the diff and from the result.
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, plain.Outcome);
        Assert.Null(plain.UntrackedFiles);
        Assert.Contains("changed tracked content", plain.CompleteDiff!, StringComparison.Ordinal);
        Assert.DoesNotContain("first untracked file", plain.CompleteDiff!, StringComparison.Ordinal);
        Assert.DoesNotContain("second untracked file", plain.CompleteDiff!, StringComparison.Ordinal);

        // After: exact bounded content, while the fingerprint, tracked diff, and paths are unchanged.
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, previews.Outcome);
        Assert.Equal(plain.FingerprintSha256, previews.FingerprintSha256);
        Assert.Equal(plain.CompleteDiff, previews.CompleteDiff);
        Assert.Equal(plain.HeadCommitSha, previews.HeadCommitSha);
        Assert.Equal(plain.ChangedPaths, previews.ChangedPaths);
        var files = previews.UntrackedFiles!;
        Assert.Equal(["notes/one.txt", "two.txt"], files.Select(file => file.Path));
        Assert.Equal("first untracked file\nwith two lines\n", files[0].Text);
        Assert.Equal("second untracked file", files[1].Text);
        Assert.All(files, file =>
        {
            Assert.Null(file.Omission);
            Assert.True(file.ContentComplete);
        });
        Assert.Equal(36L, files[0].SizeBytes);
    }

    [WindowsOnlyFact]
    public async Task One_untracked_file_and_deterministic_ordering_use_ordinal_path_order()
    {
        var repository = CreateRepository();
        Write(repository, "c1.txt", "c");
        Write(repository, "d/z.txt", "z");
        Write(repository, "a1.txt", "a");
        Write(repository, "B1.txt", "B");

        var first = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);
        var second = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);

        Assert.Equal(["B1.txt", "a1.txt", "c1.txt", "d/z.txt"], first.UntrackedFiles!.Select(file => file.Path));
        Assert.Equal(first.UntrackedFiles, second.UntrackedFiles, new UntrackedFileComparer());

        File.Delete(Path.Combine(repository, "B1.txt"));
        File.Delete(Path.Combine(repository, "a1.txt"));
        File.Delete(Path.Combine(repository, "c1.txt"));
        var one = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);
        Assert.Equal("z", Assert.Single(one.UntrackedFiles!).Text);
    }

    [WindowsOnlyFact]
    public async Task Clean_and_tracked_only_captures_have_no_previews_and_an_unchanged_fingerprint()
    {
        var repository = CreateRepository();

        var clean = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);
        var cleanPlain = await _reader.CaptureAsync(repository, CancellationToken.None);
        File.WriteAllText(Path.Combine(repository, "tracked.txt"), "tracked edit");
        var trackedOnly = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);
        var trackedOnlyPlain = await _reader.CaptureAsync(repository, CancellationToken.None);

        Assert.Empty(clean.UntrackedFiles!);
        Assert.Empty(trackedOnly.UntrackedFiles!);
        Assert.Equal(cleanPlain.FingerprintSha256, clean.FingerprintSha256);
        Assert.Equal(trackedOnlyPlain.FingerprintSha256, trackedOnly.FingerprintSha256);
        Assert.Equal(trackedOnlyPlain.CompleteDiff, trackedOnly.CompleteDiff);
    }

    [WindowsOnlyTheory]
    [InlineData(4096, 4096, true)]
    [InlineData(4097, 4096, false)]
    public async Task A_file_is_complete_only_when_it_fits_the_per_file_bound(int bytes, int expectedCharacters, bool complete)
    {
        var repository = CreateRepository();
        Write(repository, "f.txt", new string('x', bytes));

        var file = Assert.Single((await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None)).UntrackedFiles!);

        Assert.Null(file.Omission);
        Assert.Equal(expectedCharacters, file.Text!.Length);
        Assert.Equal(complete, file.ContentComplete);
        Assert.Equal((long)bytes, file.SizeBytes);
    }

    [WindowsOnlyTheory]
    [InlineData("é", 4095, 4095)]
    [InlineData("𝄞", 4094, 4094)]
    public async Task A_shortened_preview_never_splits_a_multibyte_character(string character, int asciiPrefix, int expectedCharacters)
    {
        var repository = CreateRepository();
        var content = new string('a', asciiPrefix) + character + new string('b', 10);
        Write(repository, "u.txt", content);

        var file = Assert.Single((await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None)).UntrackedFiles!);

        Assert.Equal(expectedCharacters, file.Text!.Length);
        Assert.StartsWith(file.Text, content, StringComparison.Ordinal);
        Assert.False(file.ContentComplete);
        Assert.True(Encoding.UTF8.GetByteCount(file.Text) <= 4096);
        Assert.Equal((long)Encoding.UTF8.GetByteCount(content), file.SizeBytes);
    }

    [WindowsOnlyFact]
    public async Task Bytes_are_previewed_exactly_including_a_byte_order_mark_and_crlf_line_endings()
    {
        var repository = CreateRepository();
        File.WriteAllBytes(Path.Combine(repository, "bom.txt"), [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i', 13, 10]);

        var file = Assert.Single((await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None)).UntrackedFiles!);

        Assert.Equal("﻿hi\r\n", file.Text);
        Assert.True(file.ContentComplete);
    }

    [WindowsOnlyFact]
    public async Task The_verified_size_bound_is_inclusive_and_a_larger_file_is_omitted_with_its_size()
    {
        var repository = CreateRepository();
        Write(repository, "edge.txt", new string('e', 64 * 1024));
        Write(repository, "over.txt", new string('o', 64 * 1024 + 1));

        var files = (await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None)).UntrackedFiles!;

        var edge = Assert.Single(files, file => file.Path == "edge.txt");
        Assert.Null(edge.Omission);
        Assert.False(edge.ContentComplete);
        Assert.Equal(4096, edge.Text!.Length);
        var over = Assert.Single(files, file => file.Path == "over.txt");
        Assert.Equal(GitWorkspaceUntrackedOmission.TooLarge, over.Omission);
        Assert.Equal(64L * 1024 + 1, over.SizeBytes);
        Assert.Null(over.Text);
    }

    [WindowsOnlyFact]
    public async Task The_aggregate_bound_stops_previews_and_marks_later_files_omitted()
    {
        var repository = CreateRepository();
        for (var index = 1; index <= 5; index++)
        {
            Write(repository, $"f{index}.txt", new string((char)('0' + index), 5000));
        }

        var files = (await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None)).UntrackedFiles!;

        Assert.Equal(["f1.txt", "f2.txt", "f3.txt", "f4.txt", "f5.txt"], files.Select(file => file.Path));
        Assert.All(files.Take(4), file =>
        {
            Assert.Equal(4096, file.Text!.Length);
            Assert.False(file.ContentComplete);
        });
        Assert.Equal(GitWorkspaceUntrackedOmission.AggregateLimit, files[4].Omission);
        Assert.Equal(16 * 1024, files.Sum(file => file.Text is null ? 0 : Encoding.UTF8.GetByteCount(file.Text)));
    }

    [WindowsOnlyFact]
    public async Task Binary_invalid_utf8_and_ignored_files_are_omitted_or_absent_without_leaking_bytes()
    {
        var repository = CreateRepository();
        File.WriteAllText(Path.Combine(repository, ".gitignore"), "ignored.txt\n");
        File.WriteAllBytes(Path.Combine(repository, "bin.dat"), [(byte)'a', 0, (byte)'b']);
        File.WriteAllBytes(Path.Combine(repository, "bad.txt"), [(byte)'a', 0xC3, 0x28]);
        Write(repository, "ignored.txt", "ignored content");
        Write(repository, "plain.txt", "plain");

        var result = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);
        var files = result.UntrackedFiles!;

        Assert.DoesNotContain(files, file => file.Path == "ignored.txt");
        Assert.DoesNotContain(result.ChangedPaths, path => path.Path == "ignored.txt");
        Assert.Equal(GitWorkspaceUntrackedOmission.InvalidUtf8, Assert.Single(files, file => file.Path == "bad.txt").Omission);
        Assert.Equal(GitWorkspaceUntrackedOmission.Binary, Assert.Single(files, file => file.Path == "bin.dat").Omission);
        Assert.Equal("plain", Assert.Single(files, file => file.Path == "plain.txt").Text);
        Assert.All(files.Where(file => file.Omission is not null), file => Assert.Null(file.Text));
    }

    [WindowsOnlyFact]
    public async Task A_file_changed_after_its_identity_was_captured_is_omitted_and_the_fingerprint_keeps_the_captured_identity()
    {
        var repository = CreateRepository();
        var path = Write(repository, "race.txt", "original content");
        Write(repository, "stable.txt", "stable content");
        var before = await _reader.CaptureAsync(repository, CancellationToken.None);
        var racing = new GitWorkspaceEvidenceReader(new MutatingAdapter(
            new ChildProcessExecutionAdapter(), () => File.WriteAllText(path, "replaced content!")));

        var result = await racing.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Equal(before.FingerprintSha256, result.FingerprintSha256);
        var raced = Assert.Single(result.UntrackedFiles!, file => file.Path == "race.txt");
        Assert.Equal(GitWorkspaceUntrackedOmission.ContentIdentityMismatch, raced.Omission);
        Assert.Null(raced.Text);
        Assert.DoesNotContain("replaced content", string.Join('\n', result.UntrackedFiles!.Select(file => file.Text)), StringComparison.Ordinal);
        Assert.Equal("stable content", Assert.Single(result.UntrackedFiles!, file => file.Path == "stable.txt").Text);
    }

    [WindowsOnlyFact]
    public async Task A_tracked_change_during_capture_discards_the_capture_with_no_previews()
    {
        var repository = CreateRepository();
        Write(repository, "new.txt", "untracked");
        var counter = 0;
        var racing = new GitWorkspaceEvidenceReader(new MutatingAdapter(
            new ChildProcessExecutionAdapter(),
            () => File.WriteAllText(Path.Combine(repository, "tracked.txt"), $"tracked edit {++counter}")));

        var result = await racing.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture, result.Outcome);
        Assert.Null(result.FingerprintSha256);
        Assert.Null(result.UntrackedFiles);
    }

    [WindowsOnlyFact]
    public async Task A_legitimately_redirected_worktree_root_is_accepted_against_its_resolved_identity()
    {
        var repository = CreateRepository();
        Write(repository, "new.txt", "through the junction");
        var redirected = CreateJunction(Path.Combine(_root, "redirected"), repository);

        var result = await _reader.CaptureWithUntrackedPreviewsAsync(redirected, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        var file = Assert.Single(result.UntrackedFiles!);
        Assert.Null(file.Omission);
        Assert.Equal("through the junction", file.Text);
        Assert.Equal(
            (await _reader.CaptureAsync(repository, CancellationToken.None)).FingerprintSha256, result.FingerprintSha256);
    }

    [WindowsOnlyFact]
    public async Task A_junction_inside_the_worktree_never_yields_text_from_outside_it()
    {
        var repository = CreateRepository();
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), OutsideSecret);
        CreateJunction(Path.Combine(repository, "link"), outside);
        Write(repository, "inside.txt", "inside");

        var result = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);

        if (result.Outcome == GitWorkspaceEvidenceOutcome.Success)
        {
            var files = result.UntrackedFiles!;
            Assert.DoesNotContain(OutsideSecret, string.Join('\n', files.Select(file => file.Text)), StringComparison.Ordinal);
            Assert.All(files.Where(file => file.Path.StartsWith("link", StringComparison.Ordinal)), file =>
            {
                Assert.NotNull(file.Omission);
                Assert.Null(file.Text);
            });
            Assert.Equal("inside", Assert.Single(files, file => file.Path == "inside.txt").Text);
        }
        else
        {
            Assert.Null(result.UntrackedFiles);
        }
    }

    [Fact]
    public async Task A_host_without_a_physical_containment_proof_omits_every_preview_and_keeps_the_fingerprint()
    {
        var repository = CreateRepository();
        Write(repository, "b.txt", "b");
        Write(repository, "a.txt", "a");
        var unproven = new GitWorkspaceEvidenceReader(new ChildProcessExecutionAdapter(), physicalContainmentAvailable: false);

        var result = await unproven.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);
        var plain = await _reader.CaptureAsync(repository, CancellationToken.None);

        Assert.Equal(plain.FingerprintSha256, result.FingerprintSha256);
        Assert.Equal(["a.txt", "b.txt"], result.UntrackedFiles!.Select(file => file.Path));
        Assert.All(result.UntrackedFiles!, file =>
        {
            Assert.Equal(GitWorkspaceUntrackedOmission.ContainmentUnproven, file.Omission);
            Assert.Null(file.Text);
            Assert.Null(file.SizeBytes);
        });
    }

    // ---- physically proven single-name files (ADR-0022) ---------------------------------------------------------------------

    private const string WithPreviews = "CaptureWithUntrackedPreviewsAsync";
    private const string ForAgentContext = "CaptureForAgentContextAsync";

    public static TheoryData<string> EntryPoints => new() { WithPreviews, ForAgentContext };

    private static Task<GitWorkspaceEvidenceResult> CaptureVia(string entryPoint, GitWorkspaceEvidenceReader reader, string repository) =>
        entryPoint == WithPreviews
            ? reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None)
            : reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

    private string OutsideSecretFile(string content = OutsideSecret)
    {
        var path = Path.Combine(_root, "outside-secret.txt");
        File.WriteAllText(path, content);
        return path;
    }

    [WindowsOnlyTheory]
    [MemberData(nameof(EntryPoints))]
    public async Task An_untracked_hard_link_to_an_outside_file_is_omitted_at_a_root_and_a_nested_path_while_siblings_are_previewed(string entryPoint)
    {
        var repository = CreateRepository();
        var outside = OutsideSecretFile();
        HardLinkSupport.Create(Path.Combine(repository, "linked.txt"), outside);
        Directory.CreateDirectory(Path.Combine(repository, "deep", "nested"));
        HardLinkSupport.Create(Path.Combine(repository, "deep", "nested", "linked.txt"), outside);
        Write(repository, "safe.txt", "safe sibling");
        Write(repository, "deep/control.txt", "single name control");
        var plain = await _reader.CaptureAsync(repository, CancellationToken.None);

        var result = await CaptureVia(entryPoint, _reader, repository);

        // The parent defect's precondition: Git itself identifies the linked paths by the outside bytes.
        Assert.Equal(GitOutput(repository, "hash-object", "--no-filters", "--", outside), GitOutput(repository, "hash-object", "--no-filters", "--", "linked.txt"));
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Equal(plain.FingerprintSha256, result.FingerprintSha256);
        var files = result.UntrackedFiles!;
        Assert.Equal(["deep/control.txt", "deep/nested/linked.txt", "linked.txt", "safe.txt"], files.Select(file => file.Path));
        foreach (var linked in files.Where(file => file.Path.EndsWith("linked.txt", StringComparison.Ordinal)))
        {
            Assert.Equal(GitWorkspaceUntrackedOmission.ContainmentUnproven, linked.Omission);
            Assert.Null(linked.Text);
            Assert.Null(linked.SizeBytes);
        }

        Assert.Equal("safe sibling", Assert.Single(files, file => file.Path == "safe.txt").Text);
        Assert.Equal("single name control", Assert.Single(files, file => file.Path == "deep/control.txt").Text);
        Assert.DoesNotContain(OutsideSecret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [WindowsOnlyTheory]
    [MemberData(nameof(EntryPoints))]
    public async Task An_untracked_file_with_a_second_name_inside_the_worktree_is_omitted_under_both_names(string entryPoint)
    {
        var repository = CreateRepository();
        Write(repository, "one.txt", "two names");
        HardLinkSupport.Create(Path.Combine(repository, "two.txt"), Path.Combine(repository, "one.txt"));
        Write(repository, "safe.txt", "safe sibling");

        var files = (await CaptureVia(entryPoint, _reader, repository)).UntrackedFiles!;

        Assert.All(files.Where(file => file.Path is "one.txt" or "two.txt"), file =>
        {
            Assert.Equal(GitWorkspaceUntrackedOmission.ContainmentUnproven, file.Omission);
            Assert.Null(file.Text);
            Assert.Null(file.SizeBytes);
        });
        Assert.Equal("safe sibling", Assert.Single(files, file => file.Path == "safe.txt").Text);
    }

    [WindowsOnlyTheory]
    [MemberData(nameof(EntryPoints))]
    public async Task Unavailable_admission_facts_omit_every_file_without_text_or_size_and_keep_the_fingerprint(string entryPoint)
    {
        var repository = CreateRepository();
        Write(repository, "a.txt", "a");
        Write(repository, "b.txt", "b");
        var unavailable = new GitWorkspaceEvidenceReader(
            new ChildProcessExecutionAdapter(), physicalContainmentAvailable: true, readHandleFacts: _ => null);

        var result = await CaptureVia(entryPoint, unavailable, repository);

        Assert.Equal((await _reader.CaptureAsync(repository, CancellationToken.None)).FingerprintSha256, result.FingerprintSha256);
        Assert.Equal(["a.txt", "b.txt"], result.UntrackedFiles!.Select(file => file.Path));
        Assert.All(result.UntrackedFiles!, file =>
        {
            Assert.Equal(GitWorkspaceUntrackedOmission.ContainmentUnproven, file.Omission);
            Assert.Null(file.Text);
            Assert.Null(file.SizeBytes);
        });
    }

    [WindowsOnlyTheory]
    [MemberData(nameof(EntryPoints))]
    public async Task A_second_name_that_appears_during_the_bounded_read_is_caught_by_the_final_recheck_on_the_held_handle(string entryPoint)
    {
        var repository = CreateRepository();
        Write(repository, "a-late.txt", "bytes already read when the second name appears");
        Write(repository, "b-sibling.txt", "healthy sibling");
        var calls = 0;
        var racing = new GitWorkspaceEvidenceReader(
            new ChildProcessExecutionAdapter(),
            physicalContainmentAvailable: true,
            readHandleFacts: handle =>
            {
                // Call 1 is the admission proof before any byte is read; call 2 is the recheck after the bounded read. A REAL
                // second name appears in between, so the facts the operating system reports for the same handle change.
                if (++calls == 2)
                {
                    HardLinkSupport.Create(Path.Combine(_root, "late-alias.txt"), Path.Combine(repository, "a-late.txt"));
                }

                return OperatingSystem.IsWindows() ? WindowsHandleFileFacts.TryGet(handle) : null;
            });

        var result = await CaptureVia(entryPoint, racing, repository);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        var late = Assert.Single(result.UntrackedFiles!, file => file.Path == "a-late.txt");
        Assert.Equal(GitWorkspaceUntrackedOmission.ContainmentUnproven, late.Omission);
        Assert.Null(late.Text);
        Assert.Null(late.SizeBytes);
        Assert.Equal("healthy sibling", Assert.Single(result.UntrackedFiles!, file => file.Path == "b-sibling.txt").Text);
        Assert.True(File.Exists(Path.Combine(_root, "late-alias.txt")));
    }

    [WindowsOnlyTheory]
    [MemberData(nameof(EntryPoints))]
    public async Task Admission_is_proven_twice_on_the_handle_and_a_healthy_single_name_file_is_still_previewed(string entryPoint)
    {
        var repository = CreateRepository();
        Write(repository, "only.txt", "one name");
        var calls = 0;
        var counting = new GitWorkspaceEvidenceReader(
            new ChildProcessExecutionAdapter(), physicalContainmentAvailable: true,
            readHandleFacts: handle =>
            {
                calls++;
                return OperatingSystem.IsWindows() ? WindowsHandleFileFacts.TryGet(handle) : null;
            });

        var file = Assert.Single((await CaptureVia(entryPoint, counting, repository)).UntrackedFiles!);

        Assert.Null(file.Omission);
        Assert.Equal("one name", file.Text);
        Assert.Equal(2, calls);
    }

    [WindowsOnlyTheory]
    [MemberData(nameof(EntryPoints))]
    public async Task A_host_without_a_containment_proof_omits_a_hard_linked_file_as_well_as_every_other(string entryPoint)
    {
        var repository = CreateRepository();
        HardLinkSupport.Create(Path.Combine(repository, "linked.txt"), OutsideSecretFile());
        Write(repository, "safe.txt", "safe sibling");
        var unproven = new GitWorkspaceEvidenceReader(new ChildProcessExecutionAdapter(), physicalContainmentAvailable: false);

        var result = await CaptureVia(entryPoint, unproven, repository);

        Assert.All(result.UntrackedFiles!, file =>
        {
            Assert.Equal(GitWorkspaceUntrackedOmission.ContainmentUnproven, file.Omission);
            Assert.Null(file.Text);
            Assert.Null(file.SizeBytes);
        });
        Assert.DoesNotContain(OutsideSecret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [WindowsOnlyTheory]
    [MemberData(nameof(EntryPoints))]
    public async Task Junction_and_redirected_root_controls_hold_through_both_entry_points(string entryPoint)
    {
        var repository = CreateRepository();
        var outside = Path.Combine(_root, "outside-dir");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), OutsideSecret);
        CreateJunction(Path.Combine(repository, "link"), outside);
        Write(repository, "inside.txt", "inside");
        var redirected = CreateJunction(Path.Combine(_root, "redirected-root"), repository);

        var direct = await CaptureVia(entryPoint, _reader, repository);
        var viaRedirect = await CaptureVia(entryPoint, _reader, redirected);

        foreach (var result in new[] { direct, viaRedirect })
        {
            Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
            Assert.DoesNotContain(OutsideSecret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
            Assert.Equal("inside", Assert.Single(result.UntrackedFiles!, file => file.Path == "inside.txt").Text);
            Assert.All(result.UntrackedFiles!.Where(file => file.Path.StartsWith("link", StringComparison.Ordinal)), file =>
            {
                Assert.NotNull(file.Omission);
                Assert.Null(file.Text);
            });
        }
    }

    private string CreateRepository()
    {
        var path = Path.Combine(_root, "repository");
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-q");
        RunGit(path, "config", "user.email", "test@example.com");
        RunGit(path, "config", "user.name", "Test");
        File.WriteAllText(Path.Combine(path, "tracked.txt"), "original");
        RunGit(path, "add", "tracked.txt");
        RunGit(path, "commit", "-q", "-m", "initial");
        return path;
    }

    private static string Write(string repository, string relativePath, string content)
    {
        var path = Path.Combine(repository, relativePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
        return path;
    }

    private string CreateJunction(string junctionPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junctionPath);
        startInfo.ArgumentList.Add(targetPath);
        using var process = Process.Start(startInfo)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        _junctions.Add(junctionPath);
        return junctionPath;
    }

    internal static void RunGit(string workingDirectory, params string[] arguments) => _ = GitOutput(workingDirectory, arguments);

    internal static string GitOutput(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
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
        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return output.Trim();
    }

    /// <summary>Runs the real adapter, then performs one side effect once after the first
    /// <c>hash-object</c> call (or after every one when the action is meant to keep changing state).</summary>
    private sealed class MutatingAdapter(IProcessExecutionAdapter inner, Action afterHashObject) : IProcessExecutionAdapter
    {
        public async Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            var result = await inner.ExecuteAsync(request, cancellationToken);
            if (request.Arguments.Contains("hash-object"))
            {
                afterHashObject();
            }

            return result;
        }
    }

    private sealed class UntrackedFileComparer : IEqualityComparer<GitWorkspaceUntrackedFile>
    {
        public bool Equals(GitWorkspaceUntrackedFile? x, GitWorkspaceUntrackedFile? y) => x == y;

        public int GetHashCode(GitWorkspaceUntrackedFile obj) => obj.GetHashCode();
    }
}
