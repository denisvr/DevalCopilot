using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>Real Git plus the real Windows filesystem: the attested tracked before/after text of a new Agent-context capture (ADR-0024).
/// Every tracked changed path is exactly one fact (attested text or a fixed omission); the current side is read only through a held,
/// proven handle, the baseline only from the captured HEAD's exact blob, and the raw working-path patch is never returned. Expected
/// texts are independent literals. Each disposable repository lives under a temporary root removed, junctions first, on disposal.</summary>
[Collection(EnvironmentPathMutationCollection.Name)]
[SupportedOSPlatform("windows")]
public sealed class GitWorkspaceTrackedAttestationTests : IDisposable
{
    private const string Secret = "OUTSIDE-SECRET: bytes that must never reach a capture.";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-attested-{Guid.NewGuid():N}");
    private readonly List<string> _junctions = [];
    private readonly GitWorkspaceEvidenceReader _reader = new(new ChildProcessExecutionAdapter());

    public GitWorkspaceTrackedAttestationTests() => Directory.CreateDirectory(_root);

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

    // ---- ordinary tracked changes ----------------------------------------------------------------------------------------------

    [WindowsOnlyFact]
    public async Task Edits_additions_deletions_empty_files_and_exact_line_endings_become_exact_attested_facts_in_ordinal_order()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "one\ntwo\n");
        Commit(repository, "b.txt", "unchanged\n");
        Commit(repository, "c.txt", "to be deleted\n");
        Commit(repository, "d.txt", string.Empty);
        Commit(repository, "e.txt", "x\r\ny\r\n");
        Commit(repository, "f.txt", "no final newline");
        Commit(repository, "g.txt", "staged then edited\n");
        Commit(repository, "h.txt", "staged deletion\n");
        Commit(repository, "B-upper.txt", "upper\n");
        Write(repository, "a.txt", "one\nTWO\n");
        File.Delete(Path.Combine(repository, "c.txt"));
        Write(repository, "d.txt", "now has text\n");
        Write(repository, "e.txt", "x\ny\r\n");
        Write(repository, "f.txt", "no final newline\n");
        Write(repository, "g.txt", "staged edit\n");
        Git(repository, "add", "g.txt");
        Write(repository, "g.txt", "staged edit\nthen worktree edit\n");
        Git(repository, "rm", "-q", "h.txt");
        Write(repository, "new-staged.txt", "added to the index\n");
        Git(repository, "add", "new-staged.txt");
        Write(repository, "empty-new.txt", string.Empty);
        Git(repository, "add", "empty-new.txt");
        Write(repository, "B-upper.txt", "UPPER\n");

        var ordinary = await _reader.CaptureAsync(repository, CancellationToken.None);
        var result = await Capture(repository);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Equal(ordinary.FingerprintSha256, result.FingerprintSha256);
        Assert.Equal(ordinary.HeadCommitSha, result.HeadCommitSha);
        Assert.Equal(ordinary.ChangedPaths, result.ChangedPaths);
        Assert.Null(result.CompleteDiff);
        Assert.Equal(
            new GitWorkspaceTrackedFile[]
            {
                new("B-upper.txt", null, "upper\n", "UPPER\n"),
                new("a.txt", null, "one\ntwo\n", "one\nTWO\n"),
                new("c.txt", null, "to be deleted\n", null),
                new("d.txt", null, string.Empty, "now has text\n"),
                new("e.txt", null, "x\r\ny\r\n", "x\ny\r\n"),
                new("empty-new.txt", null, null, string.Empty),
                new("f.txt", null, "no final newline", "no final newline\n"),
                new("g.txt", null, "staged then edited\n", "staged edit\nthen worktree edit\n"),
                new("h.txt", null, "staged deletion\n", null),
                new("new-staged.txt", null, null, "added to the index\n"),
            },
            result.TrackedFiles);
    }

    [WindowsOnlyFact]
    public async Task Only_tracked_changes_are_attested_untracked_and_unchanged_paths_are_not()
    {
        var repository = CreateRepository();
        Commit(repository, "tracked.txt", "t1\n");
        Commit(repository, "clean.txt", "clean\n");
        Write(repository, "tracked.txt", "t2\n");
        Write(repository, "untracked.txt", "u\n");

        var result = await Capture(repository);

        Assert.Equal(["tracked.txt"], result.TrackedFiles!.Select(file => file.Path));
    }

    [WindowsOnlyFact]
    public async Task A_clean_repository_has_an_empty_attestation_and_the_capture_is_deterministic()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "a\n");
        Write(repository, "a.txt", "b\n");

        var first = await Capture(repository);
        var second = await Capture(repository);
        Git(repository, "checkout", "--", "a.txt");
        var clean = await Capture(repository);

        Assert.Equal(first.TrackedFiles, second.TrackedFiles);
        Assert.Equal(first.FingerprintSha256, second.FingerprintSha256);
        Assert.Empty(clean.TrackedFiles!);
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, clean.Outcome);
    }

    [WindowsOnlyFact]
    public async Task Ordinary_captures_are_unchanged_they_carry_the_raw_patch_and_no_attestation()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "a\n");
        Write(repository, "a.txt", "RAW-PATCH-LINE\n");

        var plain = await _reader.CaptureAsync(repository, CancellationToken.None);
        var previews = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);

        Assert.Contains("RAW-PATCH-LINE", plain.CompleteDiff, StringComparison.Ordinal);
        Assert.Contains("RAW-PATCH-LINE", previews.CompleteDiff, StringComparison.Ordinal);
        Assert.Null(plain.TrackedFiles);
        Assert.Null(previews.TrackedFiles);
    }

    [WindowsOnlyFact]
    public async Task A_non_ascii_directory_and_a_name_with_a_space_are_attested_under_their_exact_paths()
    {
        var repository = CreateRepository();
        Commit(repository, "café dir/naïve file.txt", "before\n");
        Write(repository, "café dir/naïve file.txt", "after\n");

        var result = await Capture(repository);

        Assert.Equal(new GitWorkspaceTrackedFile("café dir/naïve file.txt", null, "before\n", "after\n"), Assert.Single(result.TrackedFiles!));
    }

    [WindowsOnlyFact]
    public async Task A_bom_multibyte_text_and_an_executable_mode_file_are_admitted_exactly()
    {
        var repository = CreateRepository();
        CommitBytes(repository, "bom.txt", [0xEF, 0xBB, 0xBF, .. "café 𝄞\n"u8.ToArray()]);
        Commit(repository, "run.sh", "#!/bin/sh\necho old\n");
        Git(repository, "update-index", "--chmod=+x", "run.sh");
        Git(repository, "commit", "-q", "-m", "make executable");
        WriteBytes(repository, "bom.txt", [0xEF, 0xBB, 0xBF, .. "CAFÉ 𝄞\n"u8.ToArray()]);
        Write(repository, "run.sh", "#!/bin/sh\necho new\n");

        var result = await Capture(repository);

        Assert.Equal(
            [
                new GitWorkspaceTrackedFile("bom.txt", null, "﻿café 𝄞\n", "﻿CAFÉ 𝄞\n"),
                new GitWorkspaceTrackedFile("run.sh", null, "#!/bin/sh\necho old\n", "#!/bin/sh\necho new\n"),
            ],
            result.TrackedFiles);
    }

    // ---- unsafe sources --------------------------------------------------------------------------------------------------------

    [WindowsOnlyFact]
    public async Task A_tracked_file_replaced_by_an_outside_hard_link_is_omitted_at_a_root_and_a_nested_path_while_siblings_stay_attested()
    {
        var repository = CreateRepository();
        var outside = OutsideFile();
        Commit(repository, "linked.txt", "committed root\n");
        Commit(repository, "deep/nested/linked.txt", "committed nested\n");
        Commit(repository, "safe.txt", "safe before\n");
        foreach (var path in new[] { "linked.txt", "deep/nested/linked.txt" })
        {
            File.Delete(Path.Combine(repository, path.Replace('/', '\\')));
            HardLinkSupport.Create(Path.Combine(repository, path.Replace('/', '\\')), outside);
        }

        Write(repository, "safe.txt", "safe after\n");
        var recorder = new RecordingAdapter(new ChildProcessExecutionAdapter());

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(recorder));

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Equal(
            [
                GitWorkspaceTrackedFile.Omitted("deep/nested/linked.txt", GitWorkspaceTrackedOmission.ContainmentUnproven),
                GitWorkspaceTrackedFile.Omitted("linked.txt", GitWorkspaceTrackedOmission.ContainmentUnproven),
                new GitWorkspaceTrackedFile("safe.txt", null, "safe before\n", "safe after\n"),
            ],
            result.TrackedFiles);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        // The unsafe sources never reached the new byte-identity operation or an object read: only the safe sibling's bytes did.
        var identity = recorder.Requests.Where(IsStdinHash).ToArray();
        Assert.Equal(2, identity.Length);
        Assert.All(identity, request => Assert.Equal("safe after\n", Encoding.UTF8.GetString(request.StandardInput!)));
        Assert.DoesNotContain(recorder.Requests, request => request.StandardInput is { } input && Encoding.UTF8.GetString(input).Contains(Secret, StringComparison.Ordinal));
        Assert.Single(recorder.Requests, IsBlobRead);
    }

    [WindowsOnlyFact]
    public async Task A_tracked_file_with_a_second_name_inside_the_worktree_is_omitted_under_both_names()
    {
        var repository = CreateRepository();
        Commit(repository, "one.txt", "one before\n");
        Commit(repository, "two.txt", "two before\n");
        Commit(repository, "safe.txt", "safe before\n");
        File.Delete(Path.Combine(repository, "two.txt"));
        HardLinkSupport.Create(Path.Combine(repository, "two.txt"), Path.Combine(repository, "one.txt"));
        Write(repository, "one.txt", "TWO NAMES " + Secret + "\n");
        Write(repository, "safe.txt", "safe after\n");

        var result = await Capture(repository);

        Assert.Equal(
            [
                GitWorkspaceTrackedFile.Omitted("one.txt", GitWorkspaceTrackedOmission.ContainmentUnproven),
                new GitWorkspaceTrackedFile("safe.txt", null, "safe before\n", "safe after\n"),
                GitWorkspaceTrackedFile.Omitted("two.txt", GitWorkspaceTrackedOmission.ContainmentUnproven),
            ],
            result.TrackedFiles);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [WindowsOnlyFact]
    public async Task Unavailable_admission_facts_omit_every_file_and_no_identity_or_object_read_is_attempted()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "a before\n");
        Commit(repository, "b.txt", "b before\n");
        Write(repository, "a.txt", "a after\n");
        Write(repository, "b.txt", "b after\n");
        var recorder = new RecordingAdapter(new ChildProcessExecutionAdapter());
        var unavailable = new GitWorkspaceEvidenceReader(recorder, physicalContainmentAvailable: true, readHandleFacts: _ => null);

        var result = await Capture(repository, unavailable);

        Assert.Equal(
            [
                GitWorkspaceTrackedFile.Omitted("a.txt", GitWorkspaceTrackedOmission.ContainmentUnproven),
                GitWorkspaceTrackedFile.Omitted("b.txt", GitWorkspaceTrackedOmission.ContainmentUnproven),
            ],
            result.TrackedFiles);
        Assert.Equal((await _reader.CaptureAsync(repository, CancellationToken.None)).FingerprintSha256, result.FingerprintSha256);
        Assert.DoesNotContain(recorder.Requests, IsStdinHash);
        Assert.DoesNotContain(recorder.Requests, IsBlobRead);
    }

    [WindowsOnlyFact]
    public async Task A_host_without_a_containment_proof_omits_every_tracked_file_and_reads_no_object()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "a before\n");
        Write(repository, "a.txt", "a after\n");
        var recorder = new RecordingAdapter(new ChildProcessExecutionAdapter());

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(recorder, physicalContainmentAvailable: false));

        Assert.Equal(GitWorkspaceTrackedOmission.ContainmentUnproven, Assert.Single(result.TrackedFiles!).Omission);
        Assert.DoesNotContain(recorder.Requests, request => request.Arguments.Contains("ls-tree") || IsBlobRead(request) || IsStdinHash(request));
    }

    [WindowsOnlyFact]
    public async Task A_parent_directory_replaced_by_a_junction_to_an_outside_directory_never_delivers_the_outside_text()
    {
        var repository = CreateRepository();
        var outside = Path.Combine(_root, "outside-dir");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "f.txt"), Secret);
        Commit(repository, "dir/f.txt", "committed\n");
        Commit(repository, "safe.txt", "safe before\n");
        Directory.Delete(Path.Combine(repository, "dir"), recursive: true);
        Junction(Path.Combine(repository, "dir"), outside);
        Write(repository, "safe.txt", "safe after\n");

        var result = await Capture(repository);

        // Git follows the junction and reports the path as modified; the held handle spells another final path, so nothing is read.
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.Equal(GitWorkspaceTrackedFile.Omitted("dir/f.txt", GitWorkspaceTrackedOmission.ContainmentUnproven), result.TrackedFiles![0]);
        Assert.Equal(new GitWorkspaceTrackedFile("safe.txt", null, "safe before\n", "safe after\n"), result.TrackedFiles[1]);
    }

    [WindowsOnlyFact]
    public async Task A_second_name_created_during_the_bounded_read_discards_the_text_and_keeps_the_healthy_sibling()
    {
        var repository = CreateRepository();
        Commit(repository, "a-late.txt", "late before\n");
        Commit(repository, "b-sibling.txt", "sibling before\n");
        Write(repository, "a-late.txt", "late after\n");
        Write(repository, "b-sibling.txt", "sibling after\n");
        var calls = 0;
        var racing = new GitWorkspaceEvidenceReader(
            new ChildProcessExecutionAdapter(),
            physicalContainmentAvailable: true,
            readHandleFacts: handle =>
            {
                if (++calls == 2)
                {
                    HardLinkSupport.Create(Path.Combine(_root, "late-alias.txt"), Path.Combine(repository, "a-late.txt"));
                }

                return WindowsHandleFileFacts.TryGet(handle);
            });

        var result = await Capture(repository, racing);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Equal(GitWorkspaceTrackedFile.Omitted("a-late.txt", GitWorkspaceTrackedOmission.ContainmentUnproven), result.TrackedFiles![0]);
        Assert.Equal(new GitWorkspaceTrackedFile("b-sibling.txt", null, "sibling before\n", "sibling after\n"), result.TrackedFiles[1]);
        Assert.True(File.Exists(Path.Combine(_root, "late-alias.txt")));
    }

    [WindowsOnlyFact]
    public async Task A_second_name_created_after_the_identity_call_is_caught_by_the_verification_proof()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "before\n");
        Write(repository, "a.txt", "after " + Secret + "\n");
        var created = false;
        var adapter = new MutatingAdapter(
            new ChildProcessExecutionAdapter(),
            request =>
            {
                // The first hash-object --stdin belongs to the in-bracket observation: the alias appears right after it.
                if (!created && IsStdinHash(request))
                {
                    created = true;
                    return true;
                }

                return false;
            },
            () => HardLinkSupport.Create(Path.Combine(_root, "alias.txt"), Path.Combine(repository, "a.txt")));

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(adapter));

        Assert.True(created);
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Equal(GitWorkspaceTrackedFile.Omitted("a.txt", GitWorkspaceTrackedOmission.ContainmentUnproven), Assert.Single(result.TrackedFiles!));
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [WindowsOnlyFact]
    public async Task A_second_name_that_appears_only_before_the_second_observation_discards_the_first_observations_text()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "before\n");
        Write(repository, "a.txt", "after " + Secret + "\n");
        var listings = 0;
        var adapter = new MutatingAdapter(
            new ChildProcessExecutionAdapter(),
            request => request.Arguments.Contains("ls-tree") && ++listings == 2,
            () => HardLinkSupport.Create(Path.Combine(_root, "alias.txt"), Path.Combine(repository, "a.txt")));

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(adapter));

        // The first observation had delivered the text; the second refused it, so the capture was retaken and nothing is left.
        Assert.True(listings >= 3);
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Equal(GitWorkspaceTrackedFile.Omitted("a.txt", GitWorkspaceTrackedOmission.ContainmentUnproven), Assert.Single(result.TrackedFiles!));
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [WindowsOnlyTheory]
    [InlineData("different-identity")]
    [InlineData("empty-identity")]
    [InlineData("failed-hash")]
    public async Task An_identity_from_git_that_does_not_match_the_bytes_that_were_read_discards_the_capture_and_delivers_nothing(string answer)
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "before\n");
        Write(repository, "a.txt", "after\n");
        IProcessExecutionAdapter adapter = answer switch
        {
            "different-identity" => new TamperingAdapter(new ChildProcessExecutionAdapter(), IsStdinHash, _ => new string('0', 40) + "\n"),
            "empty-identity" => new TamperingAdapter(new ChildProcessExecutionAdapter(), IsStdinHash, _ => string.Empty),
            _ => new FailingAdapter(new ChildProcessExecutionAdapter(), IsStdinHash, 128, string.Empty),
        };

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(adapter));

        // Bytes that cannot be independently identified are a change in the making: the capture is discarded, never an omission.
        Assert.Equal(GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture, result.Outcome);
        Assert.Null(result.TrackedFiles);
    }

    [WindowsOnlyFact]
    public async Task A_change_of_the_bytes_after_they_were_identified_discards_the_capture_when_it_persists()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "before\n");
        Write(repository, "a.txt", "after\n");
        var counter = 0;
        var adapter = new MutatingAdapter(
            new ChildProcessExecutionAdapter(), IsStdinHash, () => File.WriteAllText(Path.Combine(repository, "a.txt"), $"changing {++counter}\n"));

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(adapter));

        Assert.Equal(GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture, result.Outcome);
        Assert.Null(result.TrackedFiles);
        Assert.Null(result.FingerprintSha256);
    }

    [WindowsOnlyFact]
    public async Task A_one_time_change_is_retried_and_the_delivered_text_is_never_a_mixture()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "before\n");
        Write(repository, "a.txt", "first after\n");
        var changed = false;
        var adapter = new MutatingAdapter(
            new ChildProcessExecutionAdapter(),
            request => !changed && IsStdinHash(request),
            () =>
            {
                changed = true;
                File.WriteAllText(Path.Combine(repository, "a.txt"), "second after\n");
            });

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(adapter));

        Assert.True(changed);
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Equal(new GitWorkspaceTrackedFile("a.txt", null, "before\n", "second after\n"), Assert.Single(result.TrackedFiles!));
        Assert.Equal((await _reader.CaptureAsync(repository, CancellationToken.None)).FingerprintSha256, result.FingerprintSha256);
    }

    [WindowsOnlyFact]
    public async Task A_change_that_only_the_second_observation_sees_discards_the_first_observation()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "before\n");
        Write(repository, "a.txt", "first after\n");
        var statusCalls = 0;
        var adapter = new MutatingAdapter(
            new ChildProcessExecutionAdapter(),
            request => request.Arguments.Contains("status") && ++statusCalls == 2,
            () => File.WriteAllText(Path.Combine(repository, "a.txt"), "changed after the first observation\n"));

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(adapter));

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Equal("changed after the first observation\n", Assert.Single(result.TrackedFiles!).AfterText);
    }

    [WindowsOnlyFact]
    public async Task A_deleted_file_that_reappears_under_its_reported_name_is_never_delivered_as_a_deletion()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "before\n");
        File.Delete(Path.Combine(repository, "a.txt"));
        var adapter = new MutatingAdapter(
            new ChildProcessExecutionAdapter(),
            request => request.Arguments.Contains("ls-tree"),
            () => File.WriteAllText(Path.Combine(repository, "a.txt"), "reappeared\n"));

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(adapter));

        // The first observation saw a file where the status said none: the capture was retaken, and what remains is the truthful state.
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Equal(new GitWorkspaceTrackedFile("a.txt", null, "before\n", "reappeared\n"), Assert.Single(result.TrackedFiles!));
    }

    // ---- unsupported and unproven states ---------------------------------------------------------------------------------------

    [WindowsOnlyFact]
    public async Task A_deleted_directory_is_not_proven_absent_and_binary_or_invalid_text_is_omitted_beside_safe_siblings()
    {
        var repository = CreateRepository();
        Commit(repository, "gone/inside.txt", "inside\n");
        CommitBytes(repository, "binary.bin", [1, 2, 3]);
        CommitBytes(repository, "text-to-binary.txt", "plain\n"u8.ToArray());
        CommitBytes(repository, "invalid.txt", "valid\n"u8.ToArray());
        CommitBytes(repository, "baseline-binary.txt", [0x61, 0x00, 0x62]);
        CommitBytes(repository, "baseline-invalid.txt", [0x61, 0xFF, 0x62]);
        Commit(repository, "safe.txt", "safe before\n");
        Directory.Delete(Path.Combine(repository, "gone"), recursive: true);
        WriteBytes(repository, "binary.bin", [9, 0, 9]);
        WriteBytes(repository, "text-to-binary.txt", [0x61, 0x00, 0x62]);
        WriteBytes(repository, "invalid.txt", [0x61, 0xFF, 0x62]);
        Write(repository, "baseline-binary.txt", "now text\n");
        Write(repository, "baseline-invalid.txt", "now valid\n");
        Write(repository, "safe.txt", "safe after\n");

        var result = await Capture(repository);

        var byPath = result.TrackedFiles!.ToDictionary(file => file.Path);
        Assert.Equal(GitWorkspaceTrackedOmission.ContainmentUnproven, byPath["gone/inside.txt"].Omission);
        Assert.Equal(GitWorkspaceTrackedOmission.Binary, byPath["binary.bin"].Omission);
        Assert.Equal(GitWorkspaceTrackedOmission.Binary, byPath["text-to-binary.txt"].Omission);
        Assert.Equal(GitWorkspaceTrackedOmission.InvalidUtf8, byPath["invalid.txt"].Omission);
        Assert.Equal(GitWorkspaceTrackedOmission.Binary, byPath["baseline-binary.txt"].Omission);
        Assert.Equal(GitWorkspaceTrackedOmission.BaselineUnverified, byPath["baseline-invalid.txt"].Omission);
        Assert.Equal("safe after\n", byPath["safe.txt"].AfterText);
        Assert.All(result.TrackedFiles!.Where(file => file.Omission is not null), file =>
        {
            Assert.Null(file.BeforeText);
            Assert.Null(file.AfterText);
        });
    }

    [WindowsOnlyFact]
    public async Task Intent_to_add_and_a_symbolic_link_baseline_are_explicit_omissions_beside_a_safe_sibling()
    {
        var repository = CreateRepository();
        Commit(repository, "safe.txt", "safe before\n");
        // A committed symbolic link recorded by its mode (this host checks it out as a plain file).
        var linkBlob = HashStdin(repository, "target.txt");
        Git(repository, "update-index", "--add", "--cacheinfo", $"120000,{linkBlob},link.txt");
        Git(repository, "commit", "-q", "-m", "symlink entry");
        Write(repository, "link.txt", "edited link text\n");
        Write(repository, "safe.txt", "safe after\n");
        Write(repository, "intent.txt", "intent\n");
        Git(repository, "add", "-N", "intent.txt");

        var result = await Capture(repository);

        var byPath = result.TrackedFiles!.ToDictionary(file => file.Path);
        Assert.Equal(GitWorkspaceTrackedOmission.UnsupportedStatus, byPath["intent.txt"].Omission);
        Assert.Equal(GitWorkspaceTrackedOmission.SymbolicLink, byPath["link.txt"].Omission);
        Assert.Equal("safe after\n", byPath["safe.txt"].AfterText);
    }

    [WindowsOnlyFact]
    public async Task An_unmerged_path_is_omitted_and_its_clean_sibling_stays_attested()
    {
        var repository = CreateRepository();
        Commit(repository, "conflict.txt", "base\n");
        Commit(repository, "safe.txt", "safe before\n");
        Git(repository, "checkout", "-q", "-b", "other");
        Commit(repository, "conflict.txt", "other side\n");
        Git(repository, "checkout", "-q", "-");
        Commit(repository, "conflict.txt", "our side\n");
        Assert.NotEqual(0, RunGit(repository, "merge", "other"));
        Write(repository, "safe.txt", "safe after\n");

        var result = await Capture(repository);

        var byPath = result.TrackedFiles!.ToDictionary(file => file.Path);
        Assert.Equal(GitWorkspaceTrackedOmission.Unmerged, byPath["conflict.txt"].Omission);
        Assert.Equal("safe after\n", byPath["safe.txt"].AfterText);
    }

    [WindowsOnlyFact]
    public async Task A_gitlink_replaced_by_a_plain_file_is_an_explicit_omission_beside_a_safe_sibling()
    {
        var repository = CreateRepository();
        Commit(repository, "safe.txt", "safe before\n");
        var head = GitOutput(repository, "rev-parse", "HEAD");
        Git(repository, "update-index", "--add", "--cacheinfo", $"160000,{head},sub");
        Git(repository, "commit", "-q", "-m", "gitlink entry");
        Write(repository, "safe.txt", "safe after\n");
        Write(repository, "sub", "a plain file where the gitlink was\n");

        var result = await Capture(repository);

        // A gitlink replaced by a plain file is a type change: a state this host does not compare.
        Assert.Equal(GitWorkspaceTrackedOmission.UnsupportedStatus, Assert.Single(result.TrackedFiles!, file => file.Path == "sub").Omission);
        Assert.Equal("safe after\n", Assert.Single(result.TrackedFiles!, file => file.Path == "safe.txt").AfterText);
    }

    // ---- baseline from the exact blob -------------------------------------------------------------------------------------------

    [WindowsOnlyFact]
    public async Task A_corrupted_loose_object_is_caught_by_the_unchanged_raw_observation_before_anything_is_attested()
    {
        var repository = CreateRepository();
        Commit(repository, "corrupt.txt", "corrupt baseline\n");
        Write(repository, "corrupt.txt", "after\n");
        Overwrite(ObjectPath(repository, "corrupt.txt"), "this is not a zlib stream"u8.ToArray());

        var result = await Capture(repository);

        // The raw status and diff read the same HEAD blob first, so the capture fails whole and delivers no attestation.
        Assert.NotEqual(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Null(result.TrackedFiles);
    }

    [WindowsOnlyTheory]
    [InlineData(128, "")]
    [InlineData(1, "")]
    [InlineData(128, "fatal: unable to read blob object")]
    public async Task A_baseline_read_that_fails_at_read_time_is_an_unavailable_baseline_and_the_sibling_survives(int exitCode, string output)
    {
        var repository = CreateRepository();
        Commit(repository, "broken.txt", "broken baseline\n");
        Commit(repository, "safe.txt", "safe before\n");
        Write(repository, "broken.txt", "after\n");
        Write(repository, "safe.txt", "safe after\n");
        var brokenBlob = GitOutput(repository, "rev-parse", "HEAD:broken.txt");
        var adapter = new FailingAdapter(
            new ChildProcessExecutionAdapter(), request => IsBlobRead(request) && request.Arguments.Contains(brokenBlob), exitCode, output);

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(adapter));

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        var byPath = result.TrackedFiles!.ToDictionary(file => file.Path);
        Assert.Equal(GitWorkspaceTrackedFile.Omitted("broken.txt", GitWorkspaceTrackedOmission.BaselineUnavailable), byPath["broken.txt"]);
        Assert.Equal(new GitWorkspaceTrackedFile("safe.txt", null, "safe before\n", "safe after\n"), byPath["safe.txt"]);
    }

    [WindowsOnlyFact]
    public async Task A_baseline_listing_that_cannot_be_obtained_for_one_name_leaves_the_others_attested()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "a before\n");
        Commit(repository, "b.txt", "b before\n");
        Write(repository, "a.txt", "a after\n");
        Write(repository, "b.txt", "b after\n");
        var adapter = new FailingAdapter(
            new ChildProcessExecutionAdapter(),
            request => request.Arguments.Contains("ls-tree") && request.Arguments.Count(argument => argument is "a.txt" or "b.txt") == 2,
            128,
            string.Empty);

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(adapter));

        // The whole-batch listing failed; each name is then asked alone and both succeed.
        Assert.Equal(
            [new GitWorkspaceTrackedFile("a.txt", null, "a before\n", "a after\n"), new GitWorkspaceTrackedFile("b.txt", null, "b before\n", "b after\n")],
            result.TrackedFiles);
    }

    [WindowsOnlyFact]
    public async Task A_replacement_object_never_changes_the_baseline()
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "real baseline\n");
        var realBlob = GitOutput(repository, "rev-parse", "HEAD:a.txt");
        var fakeBlob = HashStdin(repository, "REPLACEMENT BASELINE");
        Git(repository, "replace", realBlob, fakeBlob);
        Write(repository, "a.txt", "after\n");

        var result = await Capture(repository);

        Assert.Equal(new GitWorkspaceTrackedFile("a.txt", null, "real baseline\n", "after\n"), Assert.Single(result.TrackedFiles!));
    }

    [WindowsOnlyTheory]
    [InlineData("truncated")]
    [InlineData("appended")]
    [InlineData("same-length-substitution")]
    [InlineData("empty")]
    public async Task Baseline_bytes_that_do_not_match_the_recorded_size_and_identity_are_unverified_never_delivered(string tamper)
    {
        var repository = CreateRepository();
        Commit(repository, "a.txt", "exact baseline\n");
        Commit(repository, "safe.txt", "safe before\n");
        Write(repository, "a.txt", "after\n");
        Write(repository, "safe.txt", "safe after\n");
        var blob = GitOutput(repository, "rev-parse", "HEAD:a.txt");
        var adapter = new TamperingAdapter(
            new ChildProcessExecutionAdapter(),
            request => IsBlobRead(request) && request.Arguments.Contains(blob),
            tamper switch
            {
                "truncated" => text => text[..^2],
                "appended" => text => text + "extra",
                "same-length-substitution" => text => new string('X', text.Length),
                _ => _ => string.Empty,
            });

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(adapter));

        var byPath = result.TrackedFiles!.ToDictionary(file => file.Path);
        Assert.Equal(GitWorkspaceTrackedOmission.BaselineUnverified, byPath["a.txt"].Omission);
        Assert.Null(byPath["a.txt"].BeforeText);
        Assert.Equal("safe before\n", byPath["safe.txt"].BeforeText);
    }

    [WindowsOnlyFact]
    public async Task A_baseline_the_process_stream_cannot_reproduce_byte_for_byte_is_unverified()
    {
        var repository = CreateRepository();
        CommitBytes(repository, "a.txt", [0x61, 0x80, 0x62, 0x0A]);
        Write(repository, "a.txt", "after\n");

        var result = await Capture(repository);

        Assert.Equal(GitWorkspaceTrackedOmission.BaselineUnverified, Assert.Single(result.TrackedFiles!).Omission);
    }

    [WindowsOnlyFact]
    public async Task Baseline_reading_arguments_and_environment_are_fixed_literal_filterless_and_never_name_a_working_path()
    {
        var repository = CreateRepository();
        Commit(repository, "dir/a.txt", "before\n");
        Write(repository, "dir/a.txt", "after\n");
        var recorder = new RecordingAdapter(new ChildProcessExecutionAdapter());

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(recorder));

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        var head = result.HeadCommitSha!;
        var objectReads = recorder.Requests.Where(request => request.Arguments.Contains("ls-tree") || IsBlobRead(request)).ToArray();
        Assert.NotEmpty(objectReads);
        foreach (var request in objectReads)
        {
            Assert.Contains("--no-replace-objects", request.Arguments);
            Assert.Equal("1", request.EnvironmentVariables["GIT_NO_REPLACE_OBJECTS"]);
            Assert.Equal("1", request.EnvironmentVariables["GIT_NO_LAZY_FETCH"]);
            Assert.Equal("0", request.EnvironmentVariables["GIT_TERMINAL_PROMPT"]);
            Assert.Contains("protocol.allow=never", request.Arguments);
            Assert.DoesNotContain(request.Arguments, argument => argument.Contains(repository, StringComparison.OrdinalIgnoreCase));
        }

        var lists = recorder.Requests.Where(request => request.Arguments.Contains("ls-tree")).ToArray();
        Assert.Equal(2, lists.Length);
        var list = lists[0];
        Assert.Equal(list.Arguments, lists[1].Arguments);
        var afterPrefix = list.Arguments.SkipWhile(argument => argument != "ls-tree").ToArray();
        Assert.Equal(["ls-tree", "-z", "-l", head, "--", "dir/a.txt"], afterPrefix);
        Assert.Contains("--literal-pathspecs", list.Arguments);
        var read = Assert.Single(recorder.Requests, IsBlobRead);
        Assert.Equal(["cat-file", "blob", GitOutput(repository, "rev-parse", "HEAD:dir/a.txt")], read.Arguments.SkipWhile(argument => argument != "cat-file"));
        var hash = recorder.Requests.Where(IsStdinHash).ToArray();
        Assert.All(hash, request => Assert.Equal(["hash-object", "--no-filters", "--stdin"], request.Arguments.SkipWhile(argument => argument != "hash-object")));
        Assert.All(hash, request => Assert.DoesNotContain(request.Arguments, argument => argument.Contains("a.txt", StringComparison.Ordinal)));
        // Nothing here writes an object, applies a filter or text conversion, or names a mutating command.
        var forbidden = new[] { "-w", "--filters", "--textconv", "update-index", "add", "write-tree", "commit", "fetch", "checkout" };
        Assert.DoesNotContain(recorder.Requests, request => request.Arguments.Intersect(forbidden).Any());
    }

    // ---- repository settings and quarantined raw text ---------------------------------------------------------------------------

    [WindowsOnlyTheory]
    [InlineData("diff.noprefix")]
    [InlineData("diff.mnemonicprefix")]
    public async Task Repository_diff_prefix_settings_change_only_the_raw_observation_not_the_attested_facts(string setting)
    {
        var repository = CreateRepository();
        Commit(repository, "src/a.txt", "before\n");
        Write(repository, "src/a.txt", "after\n");
        var plainDefault = await Capture(repository);
        Git(repository, "config", setting, "true");

        var configured = await Capture(repository);
        var ordinary = await _reader.CaptureAsync(repository, CancellationToken.None);

        Assert.Equal(plainDefault.TrackedFiles, configured.TrackedFiles);
        Assert.DoesNotContain("diff --git a/", ordinary.CompleteDiff, StringComparison.Ordinal);
    }

    [WindowsOnlyFact]
    public async Task Attribute_filters_a_diff_driver_function_pattern_and_eol_settings_do_not_change_the_raw_byte_attestation()
    {
        var repository = CreateRepository();
        Write(repository, ".gitattributes", "*.txt diff=custom text eol=crlf\n");
        Git(repository, "config", "diff.custom.xfuncname", "^FUNCTION-LINE.*");
        Git(repository, "config", "core.autocrlf", "true");
        Commit(repository, ".gitattributes", "*.txt diff=custom text eol=crlf\n");
        var lines = string.Concat(Enumerable.Range(1, 30).Select(index => index == 1 ? "FUNCTION-LINE secret-ish header\n" : $"line {index}\n"));
        Commit(repository, "f.txt", lines);
        Write(repository, "f.txt", lines.Replace("line 25\n", "line 25 CHANGED\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));

        var ordinary = await _reader.CaptureAsync(repository, CancellationToken.None);
        var result = await Capture(repository);

        // The raw patch carries Git's function-text header; the attestation is the raw before/after bytes (CRLF visible as written).
        Assert.Contains("FUNCTION-LINE", ordinary.CompleteDiff, StringComparison.Ordinal);
        var fact = Assert.Single(result.TrackedFiles!);
        Assert.Equal(lines, fact.BeforeText?.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains("line 25 CHANGED\r\n", fact.AfterText, StringComparison.Ordinal);
        Assert.Null(result.CompleteDiff);
    }

    // ---- bounds ----------------------------------------------------------------------------------------------------------------

    [WindowsOnlyFact]
    public async Task A_source_exactly_at_the_byte_bound_on_both_sides_is_admitted()
    {
        var repository = CreateRepository();
        var before = Lines(256, 1024, changed: -1);
        var after = Lines(256, 1024, changed: 100);
        Assert.Equal(GitWorkspaceTrackedFile.MaxSourceBytes, Encoding.UTF8.GetByteCount(before));
        Assert.Equal(GitWorkspaceTrackedFile.MaxSourceBytes, Encoding.UTF8.GetByteCount(after));
        Commit(repository, "at-limit.txt", before);
        Write(repository, "at-limit.txt", after);

        var result = await Capture(repository);

        // Retained source is exactly 512 KiB: both bounds are inclusive.
        Assert.Equal(new GitWorkspaceTrackedFile("at-limit.txt", null, before, after), Assert.Single(result.TrackedFiles!));
    }

    [WindowsOnlyFact]
    public async Task An_oversized_baseline_is_refused_by_its_recorded_size_before_the_blob_or_the_current_file_is_read()
    {
        var repository = CreateRepository();
        var before = Lines(257, 1024, changed: -1);
        Assert.True(Encoding.UTF8.GetByteCount(before) > GitWorkspaceTrackedFile.MaxSourceBytes);
        Commit(repository, "big.txt", before);
        Write(repository, "big.txt", Lines(257, 1024, changed: 3));
        var blob = GitOutput(repository, "rev-parse", "HEAD:big.txt");
        var recorder = new RecordingAdapter(new ChildProcessExecutionAdapter());

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(recorder));

        Assert.Equal(GitWorkspaceTrackedFile.Omitted("big.txt", GitWorkspaceTrackedOmission.TooLarge), Assert.Single(result.TrackedFiles!));
        Assert.DoesNotContain(recorder.Requests, request => IsBlobRead(request) && request.Arguments.Contains(blob));
        Assert.DoesNotContain(recorder.Requests, IsStdinHash);
    }

    [WindowsOnlyFact]
    public async Task An_oversized_current_file_is_refused_before_it_is_read_or_identified()
    {
        var repository = CreateRepository();
        Commit(repository, "big.txt", "small before\n");
        Write(repository, "big.txt", Lines(257, 1024, changed: -1));
        var recorder = new RecordingAdapter(new ChildProcessExecutionAdapter());

        var result = await Capture(repository, new GitWorkspaceEvidenceReader(recorder));

        Assert.Equal(GitWorkspaceTrackedFile.Omitted("big.txt", GitWorkspaceTrackedOmission.TooLarge), Assert.Single(result.TrackedFiles!));
        Assert.DoesNotContain(recorder.Requests, IsStdinHash);
        Assert.DoesNotContain(recorder.Requests, IsBlobRead);
    }

    [WindowsOnlyFact]
    public async Task The_line_bound_is_inclusive_on_both_sides()
    {
        var repository = CreateRepository();
        var atLimit = string.Concat(Enumerable.Repeat("x\n", GitWorkspaceTrackedFile.MaxSourceLines));
        var over = string.Concat(Enumerable.Repeat("x\n", GitWorkspaceTrackedFile.MaxSourceLines + 1));
        Commit(repository, "current-over.txt", "small\n");
        Commit(repository, "baseline-over.txt", over);
        Commit(repository, "at-limit.txt", "small\n");
        Write(repository, "current-over.txt", over);
        Write(repository, "baseline-over.txt", "small\n");
        Write(repository, "at-limit.txt", atLimit);

        var result = await Capture(repository);

        var byPath = result.TrackedFiles!.ToDictionary(file => file.Path);
        Assert.Equal(GitWorkspaceTrackedOmission.TooManyLines, byPath["current-over.txt"].Omission);
        Assert.Equal(GitWorkspaceTrackedOmission.TooManyLines, byPath["baseline-over.txt"].Omission);
        Assert.Equal(atLimit, byPath["at-limit.txt"].AfterText);
    }

    [WindowsOnlyFact]
    public async Task The_retained_source_budget_is_spent_in_ordinal_order_and_the_remaining_files_say_so()
    {
        var repository = CreateRepository();
        // Each large file retains 300,000 bytes of source (both sides) for a one-line change: one fits within 512 KiB, two do not.
        foreach (var name in new[] { "a.txt", "b.txt" })
        {
            Commit(repository, name, Lines(20, 7500, changed: -1));
            Write(repository, name, Lines(20, 7500, changed: 10));
        }

        Commit(repository, "c.txt", "small before\n");
        Write(repository, "c.txt", "small after\n");

        var result = await Capture(repository);

        Assert.Equal(Lines(20, 7500, changed: 10), result.TrackedFiles![0].AfterText);
        Assert.Equal(GitWorkspaceTrackedFile.Omitted("b.txt", GitWorkspaceTrackedOmission.AggregateLimit), result.TrackedFiles[1]);
        Assert.Equal(new GitWorkspaceTrackedFile("c.txt", null, "small before\n", "small after\n"), result.TrackedFiles[2]);
    }

    [WindowsOnlyFact]
    public async Task One_hundred_twenty_eight_changed_paths_are_attested_and_one_more_fails_the_capture_as_before()
    {
        var repository = CreateRepository();
        for (var index = 0; index < 129; index++)
        {
            Commit(repository, $"f{index:D3}.txt", "before\n");
        }

        for (var index = 0; index < 128; index++)
        {
            Write(repository, $"f{index:D3}.txt", "after\n");
        }

        var atLimit = await Capture(repository);
        Write(repository, "f128.txt", "after\n");
        var overLimit = await Capture(repository);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, atLimit.Outcome);
        Assert.Equal(128, atLimit.TrackedFiles!.Count);
        Assert.All(atLimit.TrackedFiles, file => Assert.Equal("after\n", file.AfterText));
        Assert.Equal(GitWorkspaceEvidenceOutcome.EvidenceTooLarge, overLimit.Outcome);
        Assert.Null(overLimit.TrackedFiles);
    }

    // ---- the whole returned capture ---------------------------------------------------------------------------------------------

    [WindowsOnlyFact]
    public async Task The_returned_capture_never_contains_the_raw_working_path_patch_or_its_text_for_an_unsafe_file()
    {
        var repository = CreateRepository();
        Commit(repository, "linked.txt", "committed\n");
        File.Delete(Path.Combine(repository, "linked.txt"));
        HardLinkSupport.Create(Path.Combine(repository, "linked.txt"), OutsideFile());

        var ordinary = await _reader.CaptureAsync(repository, CancellationToken.None);
        var result = await Capture(repository);

        Assert.Contains(Secret, ordinary.CompleteDiff, StringComparison.Ordinal);
        Assert.Null(result.CompleteDiff);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.Equal(ordinary.FingerprintSha256, result.FingerprintSha256);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------------

    private Task<GitWorkspaceEvidenceResult> Capture(string repository, GitWorkspaceEvidenceReader? reader = null) =>
        (reader ?? _reader).CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

    private string OutsideFile()
    {
        var path = Path.Combine(_root, "outside-secret.txt");
        File.WriteAllText(path, Secret);
        return path;
    }

    /// <summary>Exactly <paramref name="count"/> lines of <paramref name="width"/> UTF-8 bytes each (terminator included); one line differs.</summary>
    private static string Lines(int count, int width, int changed) =>
        string.Concat(Enumerable.Range(0, count).Select(index => new string(index == changed ? 'Z' : 'x', width - 1) + "\n"));

    private static bool IsStdinHash(ProcessExecutionRequest request) =>
        request.Arguments.Contains("hash-object") && request.Arguments.Contains("--stdin");

    private static bool IsBlobRead(ProcessExecutionRequest request) => request.Arguments.Contains("cat-file");

    private static string ObjectPath(string repository, string trackedPath)
    {
        var id = GitOutput(repository, "rev-parse", "HEAD:" + trackedPath);
        return Path.Combine(repository, ".git", "objects", id[..2], id[2..]);
    }

    private static void Overwrite(string path, byte[] content)
    {
        File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllBytes(path, content);
    }

    private string CreateRepository(string name = "repository")
    {
        var path = Path.Combine(_root, name);
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

    private static void Write(string repository, string relativePath, string content) =>
        WriteBytes(repository, relativePath, new UTF8Encoding(false).GetBytes(content));

    private static void WriteBytes(string repository, string relativePath, byte[] content)
    {
        var path = Path.Combine(repository, relativePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private static void Commit(string repository, string relativePath, string content)
    {
        Write(repository, relativePath, content);
        Git(repository, "add", relativePath);
        Git(repository, "commit", "-q", "-m", "add " + relativePath);
    }

    private static void CommitBytes(string repository, string relativePath, byte[] content)
    {
        WriteBytes(repository, relativePath, content);
        Git(repository, "add", relativePath);
        Git(repository, "commit", "-q", "-m", "add " + relativePath);
    }

    private string Junction(string junctionPath, string targetPath)
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

    private static string HashStdin(string repository, string content)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "hash-object", "-w", "--stdin" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        process.StandardInput.Write(content);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return output;
    }

    private static void Git(string workingDirectory, params string[] arguments) => GitWorkspaceUntrackedPreviewTests.RunGit(workingDirectory, arguments);

    private static string GitOutput(string workingDirectory, params string[] arguments) =>
        GitWorkspaceUntrackedPreviewTests.GitOutput(workingDirectory, arguments);

    private static int RunGit(string workingDirectory, params string[] arguments)
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
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    private sealed class RecordingAdapter(IProcessExecutionAdapter inner) : IProcessExecutionAdapter
    {
        public List<ProcessExecutionRequest> Requests { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return inner.ExecuteAsync(request, cancellationToken);
        }
    }

    /// <summary>Runs the real adapter, then performs one side effect after every request the trigger selects.</summary>
    private sealed class MutatingAdapter(IProcessExecutionAdapter inner, Func<ProcessExecutionRequest, bool> trigger, Action action)
        : IProcessExecutionAdapter
    {
        public async Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            var result = await inner.ExecuteAsync(request, cancellationToken);
            if (trigger(request))
            {
                action();
            }

            return result;
        }
    }

    /// <summary>Runs the real adapter and replaces the answer of the selected requests with a failed exit, as a Git whose object was unreadable.</summary>
    private sealed class FailingAdapter(IProcessExecutionAdapter inner, Func<ProcessExecutionRequest, bool> trigger, int exitCode, string output)
        : IProcessExecutionAdapter
    {
        public async Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            var result = await inner.ExecuteAsync(request, cancellationToken);
            return trigger(request) ? result with { ExitCode = exitCode, StandardOutput = output } : result;
        }
    }

    /// <summary>Runs the real adapter and rewrites the standard output of the selected requests, as a Git that answered wrongly.</summary>
    private sealed class TamperingAdapter(IProcessExecutionAdapter inner, Func<ProcessExecutionRequest, bool> trigger, Func<string, string> rewrite)
        : IProcessExecutionAdapter
    {
        public async Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            var result = await inner.ExecuteAsync(request, cancellationToken);
            return trigger(request) ? result with { StandardOutput = rewrite(result.StandardOutput) } : result;
        }
    }
}
