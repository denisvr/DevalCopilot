using System.Diagnostics;
using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>Real Git plus the real Windows filesystem: the exact root instruction files of a worktree, captured inside
/// the same coherent observation as the checkpoint. Expected texts, lengths and SHA-256 values are literals or are
/// computed here from literal bytes, never taken from the production reader. Every disposable repository lives under a
/// temporary root removed (junctions first) on disposal.</summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class GitWorkspaceInstructionContextTests : IDisposable
{
    private const string OutsideSentinel = "OUTSIDE-SENTINEL: bytes that must never reach a manifest.";
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string EmptySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-instructions-{Guid.NewGuid():N}");
    private readonly List<string> _junctions = [];
    private readonly GitWorkspaceEvidenceReader _reader = new(new ChildProcessExecutionAdapter());

    public GitWorkspaceInstructionContextTests()
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
    public async Task Two_projects_each_get_exactly_their_own_root_conventions_and_nothing_from_around_them()
    {
        // A file with the same name above both repositories, and a file the AGENTS.md text points at, must never be read.
        File.WriteAllText(Path.Combine(_root, "AGENTS.md"), OutsideSentinel);
        var alpha = CreateRepository("alpha");
        Commit(alpha, "AGENTS.md", "ALPHA rules\r\nsee ../EngineeringStandards/ENGINEERING.md and docs/engineering-context.md\r\n");
        Write(alpha, "docs/engineering-context.md", OutsideSentinel);
        Write(alpha, "CLAUDE.md", "abc");
        var beta = CreateRepository("beta");
        Commit(beta, "CLAUDE.md", "BETA claude rules");

        var alphaResult = await CaptureAsync(alpha);
        var betaResult = await CaptureAsync(beta);

        var alphaAgents = Agents(alphaResult);
        Assert.Equal(GitWorkspaceInstructionStatus.Complete, alphaAgents.Status);
        Assert.Equal(
            "ALPHA rules\r\nsee ../EngineeringStandards/ENGINEERING.md and docs/engineering-context.md\r\n", alphaAgents.Text);
        Assert.Equal(Encoding.UTF8.GetByteCount(alphaAgents.Text!), alphaAgents.SizeBytes);
        Assert.Equal(Sha256Hex(alphaAgents.Text!), alphaAgents.Sha256);
        var alphaClaude = Claude(alphaResult);
        Assert.Equal(GitWorkspaceInstructionStatus.Complete, alphaClaude.Status);
        Assert.Equal("abc", alphaClaude.Text);
        Assert.Equal(3L, alphaClaude.SizeBytes);
        Assert.Equal(AbcSha256, alphaClaude.Sha256);

        Assert.Equal(GitWorkspaceInstructionStatus.Absent, Agents(betaResult).Status);
        Assert.Null(Agents(betaResult).Text);
        Assert.Equal("BETA claude rules", Claude(betaResult).Text);
        Assert.Equal(["AGENTS.md", "CLAUDE.md"], alphaResult.InstructionContext!.Files.Select(file => file.FileName));
        Assert.Equal(["AGENTS.md", "CLAUDE.md"], betaResult.InstructionContext!.Files.Select(file => file.FileName));
        AssertNoSentinel(alphaResult, betaResult);
        Assert.DoesNotContain("BETA", string.Concat(alphaResult.InstructionContext.Files.Select(file => file.Text)), StringComparison.Ordinal);
        Assert.DoesNotContain("ALPHA", string.Concat(betaResult.InstructionContext.Files.Select(file => file.Text)), StringComparison.Ordinal);
    }

    [WindowsOnlyFact]
    public async Task Tracked_clean_tracked_modified_and_untracked_sources_all_report_the_current_working_tree_bytes()
    {
        var repository = CreateRepository("mixed");
        Commit(repository, "AGENTS.md", "committed agents");
        Commit(repository, "CLAUDE.md", "committed claude");

        var clean = await CaptureAsync(repository);
        Write(repository, "AGENTS.md", "working tree agents");
        var modified = await CaptureAsync(repository);
        Git(repository, "rm", "-q", "--cached", "CLAUDE.md");
        var untracked = await CaptureAsync(repository);

        Assert.Equal("committed agents", Agents(clean).Text);
        Assert.Equal("committed claude", Claude(clean).Text);
        Assert.Equal("working tree agents", Agents(modified).Text);
        Assert.Equal(Sha256Hex("working tree agents"), Agents(modified).Sha256);
        Assert.Equal("committed claude", Claude(untracked).Text);
        Assert.Equal(GitWorkspaceInstructionStatus.Complete, Claude(untracked).Status);
    }

    [WindowsOnlyFact]
    public async Task Absence_deletion_and_an_empty_file_are_distinct_facts()
    {
        var repository = CreateRepository("states");
        Commit(repository, "AGENTS.md", "will be deleted");
        File.Delete(Path.Combine(repository, "AGENTS.md"));
        var deleted = await CaptureAsync(repository);

        var neverExisted = Agents(deleted);
        var empty = CreateRepository("empty");
        Write(empty, "CLAUDE.md", string.Empty);
        var emptyResult = await CaptureAsync(empty);

        Assert.Equal(GitWorkspaceInstructionStatus.Absent, neverExisted.Status);
        Assert.Null(neverExisted.Omission);
        Assert.Null(neverExisted.Text);
        Assert.Null(neverExisted.SizeBytes);
        Assert.Null(neverExisted.Sha256);
        Assert.Equal(GitWorkspaceInstructionStatus.Absent, Claude(deleted).Status);
        var emptyFile = Claude(emptyResult);
        Assert.Equal(GitWorkspaceInstructionStatus.Complete, emptyFile.Status);
        Assert.Equal(string.Empty, emptyFile.Text);
        Assert.Equal(0L, emptyFile.SizeBytes);
        Assert.Equal(EmptySha256, emptyFile.Sha256);
    }

    [WindowsOnlyFact]
    public async Task Ignored_and_index_flagged_files_are_omitted_without_text_but_a_tracked_file_wins_over_an_ignore_rule()
    {
        var repository = CreateRepository("flags");
        Write(repository, ".gitignore", "AGENTS.md\nCLAUDE.md\n");
        Git(repository, "add", ".gitignore");
        Git(repository, "commit", "-q", "-m", "ignore");
        Write(repository, "AGENTS.md", "ignored agents");
        Write(repository, "CLAUDE.md", "ignored claude");

        var ignored = await CaptureAsync(repository);
        Git(repository, "add", "-f", "CLAUDE.md");
        Git(repository, "commit", "-q", "-m", "claude");
        var trackedDespiteIgnore = await CaptureAsync(repository);
        Git(repository, "update-index", "--assume-unchanged", "CLAUDE.md");
        var assumeUnchanged = await CaptureAsync(repository);
        Git(repository, "update-index", "--no-assume-unchanged", "CLAUDE.md");
        Git(repository, "update-index", "--skip-worktree", "CLAUDE.md");
        var skipWorktree = await CaptureAsync(repository);

        AssertOmitted(Agents(ignored), GitWorkspaceInstructionOmission.Ignored);
        AssertOmitted(Claude(ignored), GitWorkspaceInstructionOmission.Ignored);
        Assert.Equal("ignored claude", Claude(trackedDespiteIgnore).Text);
        AssertOmitted(Claude(assumeUnchanged), GitWorkspaceInstructionOmission.IndexFlag);
        AssertOmitted(Claude(skipWorktree), GitWorkspaceInstructionOmission.IndexFlag);
        Assert.DoesNotContain(ignored.ChangedPaths, path => path.Path is "AGENTS.md" or "CLAUDE.md");
    }

    [WindowsOnlyFact]
    public async Task An_unmerged_tracked_entry_is_omitted_with_its_own_reason()
    {
        var repository = CreateRepository("conflict");
        Git(repository, "branch", "-M", "main");
        Commit(repository, "AGENTS.md", "base");
        Git(repository, "checkout", "-q", "-b", "other");
        Commit(repository, "AGENTS.md", "other side");
        Git(repository, "checkout", "-q", "main");
        Commit(repository, "AGENTS.md", "main side");
        var merge = RunGit(repository, "merge", "other");
        Assert.NotEqual(0, merge);

        var result = await CaptureAsync(repository);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        AssertOmitted(Agents(result), GitWorkspaceInstructionOmission.Unmerged);
        Assert.DoesNotContain("main side", string.Concat(result.InstructionContext!.Files.Select(file => file.Text)), StringComparison.Ordinal);
    }

    [WindowsOnlyTheory]
    [InlineData(8192, true)]
    [InlineData(8193, false)]
    public async Task The_source_bound_is_inclusive_and_an_oversized_file_is_omitted_with_only_its_length(int bytes, bool complete)
    {
        var repository = CreateRepository("bounds");
        var content = new string('x', bytes);
        Commit(repository, "AGENTS.md", content);

        var file = Agents(await CaptureAsync(repository));

        if (complete)
        {
            Assert.Equal(GitWorkspaceInstructionStatus.Complete, file.Status);
            Assert.Equal(content, file.Text);
            Assert.Equal(8192L, file.SizeBytes);
        }
        else
        {
            AssertOmitted(file, GitWorkspaceInstructionOmission.TooLarge);
            Assert.Equal(8193L, file.SizeBytes);
            Assert.Null(file.Sha256);
        }
    }

    [WindowsOnlyFact]
    public async Task The_bound_counts_utf8_bytes_and_multibyte_text_survives_exactly()
    {
        var repository = CreateRepository("multibyte");
        var exactlyAtTheBound = new string('é', 4096);
        Commit(repository, "AGENTS.md", exactlyAtTheBound);
        Commit(repository, "CLAUDE.md", "rule: \"quote\" \\ back <tag> & 𝄞 ﻿mark\r\nsecond\nthird\r\n");

        var result = await CaptureAsync(repository);

        Assert.Equal(GitWorkspaceInstructionStatus.Complete, Agents(result).Status);
        Assert.Equal(exactlyAtTheBound, Agents(result).Text);
        Assert.Equal(8192L, Agents(result).SizeBytes);
        Assert.Equal("rule: \"quote\" \\ back <tag> & 𝄞 ﻿mark\r\nsecond\nthird\r\n", Claude(result).Text);
    }

    [WindowsOnlyFact]
    public async Task A_byte_order_mark_and_crlf_line_endings_are_preserved_exactly()
    {
        var repository = CreateRepository("bom");
        File.WriteAllBytes(Path.Combine(repository, "AGENTS.md"), [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i', 13, 10]);

        var file = Agents(await CaptureAsync(repository));

        Assert.Equal("﻿hi\r\n", file.Text);
        Assert.Equal(7L, file.SizeBytes);
    }

    [WindowsOnlyFact]
    public async Task Binary_and_invalid_utf8_files_are_omitted_with_their_verified_identity_and_no_text()
    {
        var repository = CreateRepository("unreadable-text");
        byte[] binary = [(byte)'a', 0, (byte)'b'];
        byte[] invalid = [(byte)'a', 0xC3, 0x28];
        File.WriteAllBytes(Path.Combine(repository, "AGENTS.md"), binary);
        File.WriteAllBytes(Path.Combine(repository, "CLAUDE.md"), invalid);

        var result = await CaptureAsync(repository);

        AssertOmitted(Agents(result), GitWorkspaceInstructionOmission.Binary);
        Assert.Equal(3L, Agents(result).SizeBytes);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(binary)).ToLowerInvariant(), Agents(result).Sha256);
        AssertOmitted(Claude(result), GitWorkspaceInstructionOmission.InvalidUtf8);
        Assert.Equal(3L, Claude(result).SizeBytes);
    }

    [WindowsOnlyFact]
    public async Task A_directory_or_a_junction_named_like_a_source_is_not_a_regular_file_and_leaks_nothing()
    {
        var repository = CreateRepository("directories");
        Directory.CreateDirectory(Path.Combine(repository, "AGENTS.md"));
        Write(repository, "AGENTS.md/inner.txt", "inner");
        var outside = Path.Combine(_root, "outside-dir");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), OutsideSentinel);
        CreateJunction(Path.Combine(repository, "CLAUDE.md"), outside);

        var result = await CaptureAsync(repository);

        if (result.Outcome == GitWorkspaceEvidenceOutcome.Success)
        {
            AssertOmitted(Agents(result), GitWorkspaceInstructionOmission.NotRegularFile);
            AssertOmitted(Claude(result), GitWorkspaceInstructionOmission.NotRegularFile);
            AssertNoSentinel(result);
        }
        else
        {
            Assert.Null(result.InstructionContext);
        }
    }

    [RequiresFileSymlinkSupportFact]
    public async Task A_symbolic_link_source_is_never_followed_to_content_outside_the_worktree()
    {
        var repository = CreateRepository("symlink");
        var target = Path.Combine(_root, "outside.txt");
        File.WriteAllText(target, OutsideSentinel);
        File.CreateSymbolicLink(Path.Combine(repository, "AGENTS.md"), target);

        var result = await CaptureAsync(repository);

        if (result.Outcome == GitWorkspaceEvidenceOutcome.Success)
        {
            AssertOmitted(Agents(result), GitWorkspaceInstructionOmission.ContainmentUnproven);
            AssertNoSentinel(result);
        }
        else
        {
            Assert.Null(result.InstructionContext);
        }
    }

    [WindowsOnlyFact]
    public async Task A_hard_link_to_a_file_outside_the_worktree_is_not_a_single_name_source_and_is_not_read()
    {
        var repository = CreateRepository("hardlink");
        var target = Path.Combine(_root, "outside-hard.txt");
        File.WriteAllText(target, OutsideSentinel);
        CreateHardLink(Path.Combine(repository, "AGENTS.md"), target);

        var result = await CaptureAsync(repository);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        AssertOmitted(Agents(result), GitWorkspaceInstructionOmission.ContainmentUnproven);
        AssertNoSentinel(result);
    }

    [WindowsOnlyFact]
    public async Task A_differently_cased_spelling_of_a_source_is_not_the_exact_root_file()
    {
        var repository = CreateRepository("casing");
        Commit(repository, "agents.md", "lowercase file");

        var result = await CaptureAsync(repository);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Null(Agents(result).Text);
        Assert.NotEqual(GitWorkspaceInstructionStatus.Complete, Agents(result).Status);
    }

    [WindowsOnlyFact]
    public async Task A_worktree_reached_through_a_junction_is_accepted_against_its_resolved_identity()
    {
        var repository = CreateRepository("real");
        Commit(repository, "AGENTS.md", "through the junction");
        var redirected = CreateJunction(Path.Combine(_root, "redirected"), repository);

        var result = await _reader.CaptureForAgentContextAsync(redirected, includeUntrackedPreviews: true, CancellationToken.None);

        Assert.Equal("through the junction", Agents(result).Text);
    }

    [WindowsOnlyFact]
    public async Task A_source_that_changes_between_its_identity_and_its_read_discards_the_whole_capture()
    {
        var repository = CreateRepository("race");
        var path = Commit(repository, "AGENTS.md", "version zero");
        Commit(repository, "CLAUDE.md", "stable claude");
        var counter = 0;
        var racing = new GitWorkspaceEvidenceReader(new MutatingAdapter(
            new ChildProcessExecutionAdapter(), request => request.Arguments.Contains("hash-object"),
            () => File.WriteAllText(path, $"version {++counter}")));

        var result = await racing.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture, result.Outcome);
        Assert.Null(result.FingerprintSha256);
        Assert.Null(result.InstructionContext);
        Assert.Null(result.UntrackedFiles);
    }

    [WindowsOnlyFact]
    public async Task A_one_time_change_is_retried_and_only_the_final_consistent_bytes_are_ever_returned()
    {
        var repository = CreateRepository("retry");
        var path = Commit(repository, "AGENTS.md", "version one");
        var changed = false;
        var racing = new GitWorkspaceEvidenceReader(new MutatingAdapter(
            new ChildProcessExecutionAdapter(), request => request.Arguments.Contains("hash-object") && !changed,
            () =>
            {
                changed = true;
                File.WriteAllText(path, "version two");
            }));

        var result = await racing.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        Assert.Equal("version two", Agents(result).Text);
        Assert.Equal(Sha256Hex("version two"), Agents(result).Sha256);
        Assert.Equal(
            (await _reader.CaptureAsync(repository, CancellationToken.None)).FingerprintSha256, result.FingerprintSha256);
    }

    [WindowsOnlyFact]
    public async Task An_untracked_source_changed_after_the_checkpoint_identity_was_captured_never_yields_text()
    {
        var repository = CreateRepository("untracked-race");
        var path = Write(repository, "AGENTS.md", "original untracked");
        var counter = 0;
        var racing = new GitWorkspaceEvidenceReader(new MutatingAdapter(
            new ChildProcessExecutionAdapter(), request => request.Arguments.Contains("hash-object"),
            () => File.WriteAllText(path, $"replaced {++counter}")));

        var result = await racing.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture, result.Outcome);
        Assert.Null(result.InstructionContext);
    }

    [WindowsOnlyFact]
    public async Task A_change_after_the_first_observation_is_caught_by_the_second_observation_of_the_bracket()
    {
        var repository = CreateRepository("after-bracket");
        var path = Write(repository, "AGENTS.md", "first");
        var counter = 0;
        var diffCalls = 0;
        // The untracked bytes change right after the bracket's second diff, so only the repeated instruction observation
        // (and not status, HEAD, or diff) can notice; the change never settles, so the capture is refused.
        var racing = new GitWorkspaceEvidenceReader(new MutatingAdapter(
            new ChildProcessExecutionAdapter(),
            request => request.Arguments.Contains("diff") && ++diffCalls % 2 == 0,
            () => File.WriteAllText(path, $"changed {++counter}")));

        var result = await racing.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: false, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture, result.Outcome);
        Assert.Null(result.InstructionContext);
    }

    [WindowsOnlyFact]
    public async Task Ordinary_captures_carry_no_instruction_context_and_the_fingerprint_does_not_depend_on_it()
    {
        var repository = CreateRepository("ordinary");
        Commit(repository, "AGENTS.md", "tracked");
        Write(repository, "CLAUDE.md", "untracked");

        var plain = await _reader.CaptureAsync(repository, CancellationToken.None);
        var previews = await _reader.CaptureWithUntrackedPreviewsAsync(repository, CancellationToken.None);
        var forAgent = await CaptureAsync(repository);
        var forAgentWithoutPreviews = await _reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: false, CancellationToken.None);

        Assert.Null(plain.InstructionContext);
        Assert.Null(previews.InstructionContext);
        Assert.NotNull(forAgent.InstructionContext);
        Assert.Equal(plain.FingerprintSha256, forAgent.FingerprintSha256);
        // The ordinary capture keeps the raw patch; the delivered capture never carries it (ADR-0024) and attests no tracked change here.
        Assert.Equal(string.Empty, plain.CompleteDiff);
        Assert.Null(forAgent.CompleteDiff);
        Assert.Empty(forAgent.TrackedFiles!);
        Assert.Null(plain.TrackedFiles);
        Assert.Equal(plain.ChangedPaths, forAgent.ChangedPaths);
        Assert.Equal(previews.UntrackedFiles!.Select(file => file.Path), forAgent.UntrackedFiles!.Select(file => file.Path));
        Assert.Null(forAgentWithoutPreviews.UntrackedFiles);
        Assert.Equal("tracked", Agents(forAgentWithoutPreviews).Text);
    }

    [Fact]
    public async Task A_host_without_a_physical_containment_proof_omits_both_sources_and_keeps_the_fingerprint()
    {
        var repository = CreateRepository("unproven");
        Commit(repository, "AGENTS.md", "would be readable");
        var unproven = new GitWorkspaceEvidenceReader(new ChildProcessExecutionAdapter(), physicalContainmentAvailable: false);

        var result = await unproven.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);
        var plain = await _reader.CaptureAsync(repository, CancellationToken.None);

        Assert.Equal(plain.FingerprintSha256, result.FingerprintSha256);
        Assert.Equal(["AGENTS.md", "CLAUDE.md"], result.InstructionContext!.Files.Select(file => file.FileName));
        Assert.All(result.InstructionContext.Files, file =>
        {
            AssertOmitted(file, GitWorkspaceInstructionOmission.ContainmentUnproven);
            Assert.Null(file.SizeBytes);
            Assert.Null(file.Sha256);
        });
    }

    [WindowsOnlyFact]
    public async Task Only_fixed_hardened_git_arguments_with_literal_pathspecs_name_the_two_root_files()
    {
        var repository = CreateRepository("arguments");
        Commit(repository, "AGENTS.md", "x");
        var recorder = new RecordingAdapter(new ChildProcessExecutionAdapter());
        var reader = new GitWorkspaceEvidenceReader(recorder);

        _ = await reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        var instructionCalls = recorder.Requests
            .Where(request => request.Arguments.Contains("ls-files") || request.Arguments.Contains("hash-object"))
            .Where(request => request.Arguments.Contains("AGENTS.md") || request.Arguments.Contains("CLAUDE.md"))
            .ToArray();
        Assert.NotEmpty(instructionCalls);
        Assert.All(instructionCalls, request =>
        {
            Assert.Contains("--literal-pathspecs", request.Arguments);
            Assert.Contains("--no-pager", request.Arguments);
            Assert.Contains("core.fsmonitor=false", request.Arguments);
            Assert.Equal("0", request.EnvironmentVariables!["GIT_TERMINAL_PROMPT"]);
            Assert.All(
                request.Arguments.SkipWhile(argument => argument != "--").Skip(1),
                argument => Assert.Contains(argument, new[] { "AGENTS.md", "CLAUDE.md" }));
        });
    }

    // ---- the independent identity operation (R1): only bytes acquired through a proven handle ever reach Git -------------------

    private static bool IsHashObject(ProcessExecutionRequest request) => request.Arguments.Contains("hash-object");

    private static bool NamesAFile(ProcessExecutionRequest request) =>
        request.Arguments.Any(argument => argument is "AGENTS.md" or "CLAUDE.md");

    [WindowsOnlyFact]
    public async Task The_identity_of_a_tracked_source_is_computed_from_bounded_stdin_and_never_names_a_repository_path()
    {
        var repository = CreateRepository("identity-stdin");
        const string content = "tracked conventions\r\n";
        Commit(repository, "AGENTS.md", content);
        var recorder = new RecordingAdapter(new ChildProcessExecutionAdapter());

        var result = await new GitWorkspaceEvidenceReader(recorder)
            .CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        Assert.Equal(content, Agents(result).Text);
        var hashCalls = recorder.Requests.Where(IsHashObject).ToArray();
        Assert.NotEmpty(hashCalls);
        Assert.All(hashCalls, request =>
        {
            Assert.Contains("--stdin", request.Arguments);
            Assert.Contains("--no-filters", request.Arguments);
            Assert.DoesNotContain("--", request.Arguments.SkipWhile(argument => argument != "hash-object"));
            Assert.False(NamesAFile(request));
            Assert.NotNull(request.StandardInput);
            Assert.True(request.StandardInput!.Length <= 8192);
            Assert.Equal(Encoding.UTF8.GetBytes(content), request.StandardInput);
        });
    }

    [WindowsOnlyFact]
    public async Task A_tracked_hard_link_to_an_outside_file_is_never_hashed_or_sent_to_git()
    {
        var repository = CreateRepository("identity-hardlink");
        var outside = Path.Combine(_root, "outside-tracked-link.txt");
        File.WriteAllText(outside, OutsideSentinel);
        CreateHardLink(Path.Combine(repository, "AGENTS.md"), outside);
        Git(repository, "add", "AGENTS.md");
        Git(repository, "commit", "-q", "-m", "link");
        var recorder = new RecordingAdapter(new ChildProcessExecutionAdapter());

        var result = await new GitWorkspaceEvidenceReader(recorder)
            .CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        AssertOmitted(Agents(result), GitWorkspaceInstructionOmission.ContainmentUnproven);
        Assert.DoesNotContain(recorder.Requests, IsHashObject);
        Assert.DoesNotContain(recorder.Requests, request =>
            request.StandardInput is not null && Encoding.UTF8.GetString(request.StandardInput).Contains("OUTSIDE-SENTINEL", StringComparison.Ordinal));
    }

    [WindowsOnlyFact]
    public async Task A_source_over_the_bound_is_never_sent_to_git_at_all()
    {
        var repository = CreateRepository("identity-bound");
        Commit(repository, "AGENTS.md", new string('x', 8193));
        var recorder = new RecordingAdapter(new ChildProcessExecutionAdapter());

        var result = await new GitWorkspaceEvidenceReader(recorder)
            .CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        AssertOmitted(Agents(result), GitWorkspaceInstructionOmission.TooLarge);
        Assert.DoesNotContain(recorder.Requests, IsHashObject);
    }

    [WindowsOnlyFact]
    public async Task A_file_substituted_between_the_proof_and_the_identity_discards_the_capture()
    {
        var repository = CreateRepository("identity-substitution");
        var path = Commit(repository, "AGENTS.md", "proven bytes");
        var counter = 0;
        var racing = new GitWorkspaceEvidenceReader(new MutatingAdapter(
            new ChildProcessExecutionAdapter(), request => IsHashObject(request) && request.Arguments.Contains("--stdin"),
            () => File.WriteAllText(path, $"substituted {++counter}")));

        var result = await racing.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture, result.Outcome);
        Assert.Null(result.InstructionContext);
    }

    [WindowsOnlyFact]
    public async Task A_transient_substitution_during_the_identity_operation_is_refused_even_when_restored_before_the_next_observation()
    {
        var repository = CreateRepository("identity-transient");
        var path = Commit(repository, "AGENTS.md", "proven bytes");
        // The file is rewritten while Git hashes the proven bytes and put back before the second observation reads it, so the two
        // observations alone would agree; only re-reading the held handle can notice the substitution.
        var racing = new GitWorkspaceEvidenceReader(new TransientSubstitutionAdapter(
            new ChildProcessExecutionAdapter(), path, "proven bytes", "transient bytes"));

        var result = await racing.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture, result.Outcome);
        Assert.Null(result.InstructionContext);
    }

    [WindowsOnlyFact]
    public async Task A_git_identity_that_disagrees_with_the_proven_bytes_discards_the_capture()
    {
        var repository = CreateRepository("identity-disagree");
        Commit(repository, "AGENTS.md", "proven bytes");
        var lying = new GitWorkspaceEvidenceReader(new TamperingAdapter(
            new ChildProcessExecutionAdapter(), request => IsHashObject(request) && request.Arguments.Contains("--stdin"), new string('a', 40)));

        var result = await lying.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

        Assert.Equal(GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture, result.Outcome);
        Assert.Null(result.InstructionContext);
    }

    private async Task<GitWorkspaceEvidenceResult> CaptureAsync(string repository) =>
        await _reader.CaptureForAgentContextAsync(repository, includeUntrackedPreviews: true, CancellationToken.None);

    private static GitWorkspaceInstructionFile Agents(GitWorkspaceEvidenceResult result) => result.InstructionContext!.Files[0];

    private static GitWorkspaceInstructionFile Claude(GitWorkspaceEvidenceResult result) => result.InstructionContext!.Files[1];

    private static void AssertOmitted(GitWorkspaceInstructionFile file, GitWorkspaceInstructionOmission reason)
    {
        Assert.Equal(GitWorkspaceInstructionStatus.Omitted, file.Status);
        Assert.Equal(reason, file.Omission);
        Assert.Null(file.Text);
    }

    /// <summary>The instruction context never carries bytes from outside the worktree or from a file the sources merely
    /// mention. (Untracked previews are a separate, older capability and are not asserted here.)</summary>
    private static void AssertNoSentinel(params GitWorkspaceEvidenceResult[] results)
    {
        foreach (var result in results)
        {
            var everything = string.Concat(result.InstructionContext!.Files.Select(file => file.Text));
            Assert.DoesNotContain("OUTSIDE-SENTINEL", everything, StringComparison.Ordinal);
        }
    }

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private string CreateRepository(string name)
    {
        var path = Path.Combine(_root, name);
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

    private static string Write(string repository, string relativePath, string content)
    {
        var path = Path.Combine(repository, relativePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
        return path;
    }

    private static string Commit(string repository, string relativePath, string content)
    {
        var path = Write(repository, relativePath, content);
        Git(repository, "add", relativePath);
        Git(repository, "commit", "-q", "-m", $"add {relativePath}");
        return path;
    }

    private string CreateJunction(string junctionPath, string targetPath)
    {
        Cmd("mklink", "/J", junctionPath, targetPath);
        _junctions.Add(junctionPath);
        return junctionPath;
    }

    private static void CreateHardLink(string linkPath, string targetPath) => Cmd("mklink", "/H", linkPath, targetPath);

    private static void Cmd(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/c");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static void Git(string workingDirectory, params string[] arguments) =>
        Assert.Equal(0, RunGit(workingDirectory, arguments));

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

    /// <summary>Runs the real adapter, then performs one side effect after every request the trigger selects.</summary>
    private sealed class MutatingAdapter(
        IProcessExecutionAdapter inner, Func<ProcessExecutionRequest, bool> trigger, Action action) : IProcessExecutionAdapter
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

    /// <summary>Rewrites the file right after the stdin identity call and restores it after the next listing call.</summary>
    private sealed class TransientSubstitutionAdapter(IProcessExecutionAdapter inner, string path, string proven, string transient)
        : IProcessExecutionAdapter
    {
        private bool _restoreAtNextListing;

        public async Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            var result = await inner.ExecuteAsync(request, cancellationToken);
            if (IsHashObject(request) && request.Arguments.Contains("--stdin"))
            {
                File.WriteAllText(path, transient);
                _restoreAtNextListing = true;
            }
            else if (_restoreAtNextListing && request.Arguments.Contains("ls-files"))
            {
                File.WriteAllText(path, proven);
                _restoreAtNextListing = false;
            }

            return result;
        }
    }

    /// <summary>Runs the real adapter and replaces the standard output of the selected requests, as a Git that answered wrongly.</summary>
    private sealed class TamperingAdapter(IProcessExecutionAdapter inner, Func<ProcessExecutionRequest, bool> trigger, string output)
        : IProcessExecutionAdapter
    {
        public async Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            var result = await inner.ExecuteAsync(request, cancellationToken);
            return trigger(request) ? result with { StandardOutput = output + "\n" } : result;
        }
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
}
