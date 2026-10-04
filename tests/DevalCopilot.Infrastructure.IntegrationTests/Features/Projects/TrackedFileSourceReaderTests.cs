using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>The held-handle reader of CURRENT tracked bytes (ADR-0024) on the real NTFS: every proof is asked of the open handle, before
/// any length or byte, again after the bounded read and once more around the identity operation, and a deletion is claimed only under a
/// physically proven owned parent. Hard links and junctions are real; the operating system's answer about a handle is observed through
/// the same seam the capture uses.</summary>
[Collection(EnvironmentPathMutationCollection.Name)]
[SupportedOSPlatform("windows")]
public sealed class TrackedFileSourceReaderTests : IDisposable
{
    private const string Secret = "OUTSIDE-SECRET-bytes";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-tracked-source-{Guid.NewGuid():N}");
    private readonly string _workspace;
    private readonly string _rootFinal;
    private readonly List<string> _junctions = [];

    public TrackedFileSourceReaderTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);
        _rootFinal = WindowsFinalPathResolver.TryResolveDirectoryFinalPath(_workspace)!.TrimEnd('\\');
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

    private static Func<SafeFileHandle, WindowsHandleFileFacts.Facts?> Real => WindowsHandleFileFacts.TryGet;

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_workspace, relativePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
        return path;
    }

    private TrackedFileSourceReader.Source Acquire(string relativePath, Func<SafeFileHandle, WindowsHandleFileFacts.Facts?>? facts = null) =>
        TrackedFileSourceReader.Acquire(_workspace, _rootFinal, relativePath, facts ?? Real);

    private string Junction(string relativePath, string target)
    {
        var path = Path.Combine(_workspace, relativePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Cmd("mklink", "/J", path, target);
        _junctions.Add(path);
        return path;
    }

    [WindowsOnlyFact]
    public void A_single_name_regular_file_is_proven_and_its_exact_bytes_are_returned()
    {
        Write("src/a.txt", "exact bytes\r\nno newline");

        using var source = Acquire("src/a.txt");

        Assert.Equal(TrackedFileSourceReader.SourceKind.Proven, source.Kind);
        Assert.Equal(Encoding.UTF8.GetBytes("exact bytes\r\nno newline"), source.Bytes);
        Assert.Null(TrackedFileSourceReader.Verify(source, _rootFinal, "src/a.txt", Real));
    }

    [WindowsOnlyFact]
    public void The_proof_is_asked_of_the_handle_twice_before_verification_and_a_third_time_by_it()
    {
        Write("a.txt", "x");
        var calls = 0;

        using var source = Acquire("a.txt", handle =>
        {
            calls++;
            return WindowsHandleFileFacts.TryGet(handle);
        });
        Assert.Equal(2, calls);
        Assert.Null(TrackedFileSourceReader.Verify(source, _rootFinal, "a.txt", handle =>
        {
            calls++;
            return WindowsHandleFileFacts.TryGet(handle);
        }));

        Assert.Equal(3, calls);
    }

    [WindowsOnlyFact]
    public void A_hard_link_to_an_outside_file_is_refused_before_any_byte_is_read()
    {
        var outside = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outside, Secret);
        HardLinkSupport.Create(Path.Combine(_workspace, "linked.txt"), outside);
        var lengthOrBytesAsked = false;

        using var source = Acquire("linked.txt", handle =>
        {
            lengthOrBytesAsked = true;
            return WindowsHandleFileFacts.TryGet(handle);
        });

        Assert.Equal(TrackedFileSourceReader.SourceKind.Refused, source.Kind);
        Assert.Equal(GitWorkspaceTrackedOmission.ContainmentUnproven, source.Omission);
        Assert.Null(source.Bytes);
        Assert.True(lengthOrBytesAsked);
    }

    [WindowsOnlyFact]
    public void A_file_with_a_second_name_inside_the_worktree_is_refused_under_both_names()
    {
        Write("one.txt", "two names");
        HardLinkSupport.Create(Path.Combine(_workspace, "two.txt"), Path.Combine(_workspace, "one.txt"));

        foreach (var name in new[] { "one.txt", "two.txt" })
        {
            using var source = Acquire(name);
            Assert.Equal(GitWorkspaceTrackedOmission.ContainmentUnproven, source.Omission);
        }
    }

    [WindowsOnlyFact]
    public void Unavailable_handle_facts_prove_nothing()
    {
        Write("a.txt", "x");

        using var source = Acquire("a.txt", _ => null);

        Assert.Equal(GitWorkspaceTrackedOmission.ContainmentUnproven, source.Omission);
        Assert.Null(source.Bytes);
    }

    [WindowsOnlyFact]
    public void A_reparse_point_fact_is_refused_even_when_the_final_path_and_link_count_match()
    {
        Write("a.txt", "x");

        using var source = Acquire(
            "a.txt", handle => new WindowsHandleFileFacts.Facts(WindowsHandleFileFacts.TryGet(handle)!.Value.Attributes | FileAttributes.ReparsePoint, 1));

        Assert.Equal(GitWorkspaceTrackedOmission.ContainmentUnproven, source.Omission);
    }

    [WindowsOnlyFact]
    public void A_device_or_directory_fact_is_not_a_regular_file_and_nothing_is_read()
    {
        Write("a.txt", "x");

        using var device = Acquire("a.txt", handle => new WindowsHandleFileFacts.Facts(FileAttributes.Device, 1));
        using var directory = Acquire("a.txt", handle => new WindowsHandleFileFacts.Facts(FileAttributes.Directory, 1));

        Assert.Equal(GitWorkspaceTrackedOmission.NotRegularFile, device.Omission);
        Assert.Equal(GitWorkspaceTrackedOmission.NotRegularFile, directory.Omission);
    }

    [WindowsOnlyFact]
    public void A_directory_at_the_path_is_not_a_regular_file()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "dir"));

        using var source = Acquire("dir");

        Assert.Equal(GitWorkspaceTrackedOmission.NotRegularFile, source.Omission);
    }

    [WindowsOnlyFact]
    public void A_missing_path_is_not_found_and_never_a_proof_of_absence()
    {
        using var source = Acquire("nothing.txt");
        using var nestedMissing = Acquire("no/such/dir/file.txt");

        Assert.Equal(TrackedFileSourceReader.SourceKind.NotFound, source.Kind);
        Assert.Equal(TrackedFileSourceReader.SourceKind.NotFound, nestedMissing.Kind);
        Assert.Null(source.Omission);
    }

    [WindowsOnlyFact]
    public void A_file_reached_through_a_junction_directory_is_refused_because_its_final_path_is_elsewhere()
    {
        var outside = Path.Combine(_root, "outside-dir");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "f.txt"), Secret);
        Junction("dir", outside);

        using var source = Acquire("dir/f.txt");

        Assert.Equal(TrackedFileSourceReader.SourceKind.Refused, source.Kind);
        Assert.Equal(GitWorkspaceTrackedOmission.ContainmentUnproven, source.Omission);
        Assert.Null(source.Bytes);
    }

    [WindowsOnlyFact]
    public void A_case_variant_spelling_of_the_name_is_refused_because_the_handle_spells_the_real_name()
    {
        Write("Real.txt", "x");

        using var variant = Acquire("real.txt");
        using var exact = Acquire("Real.txt");

        Assert.Equal(GitWorkspaceTrackedOmission.ContainmentUnproven, variant.Omission);
        Assert.Equal(TrackedFileSourceReader.SourceKind.Proven, exact.Kind);
    }

    [WindowsOnlyFact]
    public void A_path_with_dot_segments_drive_syntax_or_streams_is_refused_unopened()
    {
        Write("a.txt", "x");

        foreach (var path in new[] { "../a.txt", "./a.txt", "a/../a.txt", "a.txt:stream", "C:/a.txt", "/a.txt", "a\\b.txt", "", "a//b.txt" })
        {
            using var source = Acquire(path);
            Assert.Equal(GitWorkspaceTrackedOmission.ContainmentUnproven, source.Omission);
        }
    }

    [WindowsOnlyFact]
    public void The_length_bound_is_inclusive_and_an_oversized_file_is_refused_before_a_byte_is_read()
    {
        File.WriteAllBytes(Path.Combine(_workspace, "limit.bin"), new byte[GitWorkspaceTrackedFile.MaxSourceBytes]);
        File.WriteAllBytes(Path.Combine(_workspace, "over.bin"), new byte[GitWorkspaceTrackedFile.MaxSourceBytes + 1]);

        using var atLimit = Acquire("limit.bin");
        using var over = Acquire("over.bin");

        Assert.Equal(TrackedFileSourceReader.SourceKind.Proven, atLimit.Kind);
        Assert.Equal(GitWorkspaceTrackedFile.MaxSourceBytes, atLimit.Bytes!.Length);
        Assert.Equal(GitWorkspaceTrackedOmission.TooLarge, over.Omission);
        Assert.Null(over.Bytes);
    }

    [WindowsOnlyFact]
    public void The_proof_comes_before_the_length_so_an_unproven_oversized_file_is_refused_as_unproven_and_never_measured()
    {
        var outside = Path.Combine(_root, "outside-big.bin");
        File.WriteAllBytes(outside, new byte[GitWorkspaceTrackedFile.MaxSourceBytes + 1]);
        HardLinkSupport.Create(Path.Combine(_workspace, "linked.bin"), outside);

        using var source = Acquire("linked.bin");

        Assert.Equal(GitWorkspaceTrackedOmission.ContainmentUnproven, source.Omission);
    }

    [WindowsOnlyFact]
    public void The_proof_is_asked_before_any_other_use_of_the_handle_and_a_first_refusal_stops_everything()
    {
        Write("a.txt", "x");
        var calls = 0;

        using var source = Acquire("a.txt", handle =>
        {
            calls++;
            return null;
        });

        Assert.Equal(GitWorkspaceTrackedOmission.ContainmentUnproven, source.Omission);
        Assert.Equal(1, calls);
    }

    [WindowsOnlyFact]
    public void A_locked_file_is_unreadable_never_absent()
    {
        var path = Write("locked.txt", "x");
        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        using var source = Acquire("locked.txt");

        Assert.Equal(GitWorkspaceTrackedOmission.Unreadable, source.Omission);
        Assert.Equal(TrackedFileSourceReader.Absence.Unreadable, TrackedFileSourceReader.ProveAbsent(_workspace, _rootFinal, "locked.txt"));
    }

    [WindowsOnlyFact]
    public void A_second_name_that_appears_after_the_read_is_caught_by_the_recheck_on_the_same_handle()
    {
        Write("a.txt", "bytes already read");
        var calls = 0;

        using var source = Acquire("a.txt", handle =>
        {
            if (++calls == 2)
            {
                HardLinkSupport.Create(Path.Combine(_root, "late-alias.txt"), Path.Combine(_workspace, "a.txt"));
            }

            return WindowsHandleFileFacts.TryGet(handle);
        });

        Assert.Equal(GitWorkspaceTrackedOmission.ContainmentUnproven, source.Omission);
        Assert.Null(source.Bytes);
    }

    [WindowsOnlyFact]
    public void A_second_name_that_appears_before_verification_is_caught_by_the_verification_proof()
    {
        var path = Write("a.txt", "bytes");
        using var source = Acquire("a.txt");
        Assert.Equal(TrackedFileSourceReader.SourceKind.Proven, source.Kind);

        HardLinkSupport.Create(Path.Combine(_root, "late-alias.txt"), path);
        var verdict = TrackedFileSourceReader.Verify(source, _rootFinal, "a.txt", Real);

        Assert.NotNull(verdict);
        Assert.Equal(TrackedFileSourceReader.SourceKind.Refused, verdict.Kind);
        Assert.Equal(GitWorkspaceTrackedOmission.ContainmentUnproven, verdict.Omission);
    }

    [WindowsOnlyFact]
    public void A_rewrite_between_the_proof_and_the_verification_is_a_change_not_an_omission()
    {
        var path = Write("a.txt", "original");
        using var source = Acquire("a.txt");

        File.WriteAllText(path, "REWRITTEN");
        var verdict = TrackedFileSourceReader.Verify(source, _rootFinal, "a.txt", Real);

        Assert.NotNull(verdict);
        Assert.Equal(TrackedFileSourceReader.SourceKind.Changed, verdict.Kind);
    }

    [WindowsOnlyFact]
    public void A_same_length_rewrite_between_the_proof_and_the_verification_is_a_change_only_the_byte_comparison_can_see()
    {
        var path = Write("a.txt", "original");
        using var source = Acquire("a.txt");

        File.WriteAllText(path, "ORIGINAL");
        var verdict = TrackedFileSourceReader.Verify(source, _rootFinal, "a.txt", Real);

        Assert.NotNull(verdict);
        Assert.Equal(TrackedFileSourceReader.SourceKind.Changed, verdict.Kind);
    }

    [WindowsOnlyFact]
    public void A_truncation_between_the_proof_and_the_verification_is_a_change()
    {
        var path = Write("a.txt", "original");
        using var source = Acquire("a.txt");

        File.WriteAllText(path, "orig");

        Assert.Equal(TrackedFileSourceReader.SourceKind.Changed, TrackedFileSourceReader.Verify(source, _rootFinal, "a.txt", Real)!.Kind);
    }

    // ---- proof of absence ------------------------------------------------------------------------------------------------------

    [WindowsOnlyFact]
    public void A_deletion_is_proven_only_under_an_owned_parent_at_the_root_and_nested()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "src", "deep"));

        Assert.Equal(TrackedFileSourceReader.Absence.Proven, TrackedFileSourceReader.ProveAbsent(_workspace, _rootFinal, "gone.txt"));
        Assert.Equal(TrackedFileSourceReader.Absence.Proven, TrackedFileSourceReader.ProveAbsent(_workspace, _rootFinal, "src/deep/gone.txt"));
    }

    [WindowsOnlyFact]
    public void A_present_file_directory_or_missing_parent_is_never_a_proven_deletion()
    {
        Write("here.txt", "x");
        Directory.CreateDirectory(Path.Combine(_workspace, "dir"));

        Assert.Equal(TrackedFileSourceReader.Absence.Present, TrackedFileSourceReader.ProveAbsent(_workspace, _rootFinal, "here.txt"));
        Assert.Equal(TrackedFileSourceReader.Absence.NotRegularFile, TrackedFileSourceReader.ProveAbsent(_workspace, _rootFinal, "dir"));
        Assert.Equal(TrackedFileSourceReader.Absence.Unproven, TrackedFileSourceReader.ProveAbsent(_workspace, _rootFinal, "no/such/parent/gone.txt"));
    }

    [WindowsOnlyFact]
    public void A_deletion_under_a_substituted_junction_parent_is_not_proven()
    {
        var outside = Path.Combine(_root, "outside-dir");
        Directory.CreateDirectory(outside);
        Junction("dir", outside);

        Assert.Equal(TrackedFileSourceReader.Absence.Unproven, TrackedFileSourceReader.ProveAbsent(_workspace, _rootFinal, "dir/gone.txt"));
    }

    [WindowsOnlyFact]
    public void A_case_variant_of_a_deleted_name_that_exists_is_unproven_not_a_change_and_not_absent()
    {
        Write("Real.txt", "x");

        Assert.Equal(TrackedFileSourceReader.Absence.Unproven, TrackedFileSourceReader.ProveAbsent(_workspace, _rootFinal, "real.txt"));
    }

    [WindowsOnlyFact]
    public void A_hard_link_standing_where_a_deletion_is_claimed_is_present_and_a_path_syntax_trick_is_unproven()
    {
        var outside = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outside, Secret);
        HardLinkSupport.Create(Path.Combine(_workspace, "now-here.txt"), outside);

        Assert.Equal(TrackedFileSourceReader.Absence.Present, TrackedFileSourceReader.ProveAbsent(_workspace, _rootFinal, "now-here.txt"));
        Assert.Equal(TrackedFileSourceReader.Absence.Unproven, TrackedFileSourceReader.ProveAbsent(_workspace, _rootFinal, "../outside.txt"));
    }

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
}
