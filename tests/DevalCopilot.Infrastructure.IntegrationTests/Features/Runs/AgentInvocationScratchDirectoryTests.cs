using System.Diagnostics;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises <see cref="AgentInvocationScratchDirectory.TryDelete"/> directly, in isolation from
/// the rest of <see cref="CodexPlanningAdapter"/>. The type is <c>internal</c> but visible here
/// through this assembly's existing <c>InternalsVisibleTo</c> grant to this test project — the
/// same grant <see cref="CodexPlanningAdapterTests"/> already relies on to call
/// <see cref="AgentInvocationScratchDirectory.EnsureExists"/> directly. That suite already proves
/// <c>TryDelete</c> runs as part of a full invocation; this file isolates the method's own two
/// contractually distinct behaviors: full recursive deletion of a genuine scratch directory
/// (a regression guard), and never recursing through a reparse-point scratch directory into
/// content this application does not own.
/// </summary>
public sealed class AgentInvocationScratchDirectoryTests : IDisposable
{
    private readonly string _scratchWorkspace =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-scratch-directory-tests-{Guid.NewGuid():N}");

    public AgentInvocationScratchDirectoryTests()
    {
        Directory.CreateDirectory(_scratchWorkspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratchWorkspace))
        {
            Directory.Delete(_scratchWorkspace, recursive: true);
        }
    }

    [Fact]
    public void TryDelete_fully_and_recursively_removes_a_genuine_non_reparse_point_scratch_directory()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var scratchDirectory = AgentInvocationScratchDirectory.EnsureExists(runId, attemptId);

        var nestedDirectory = Path.Combine(scratchDirectory, "nested");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(Path.Combine(scratchDirectory, "schema.json"), "{}");
        File.WriteAllText(Path.Combine(nestedDirectory, "nested-file.txt"), "nested content");

        AgentInvocationScratchDirectory.TryDelete(runId, attemptId);

        // This must not regress: an ordinary, reparse-point-free scratch directory (and
        // everything nested inside it) is still fully removed, exactly as before this
        // correction.
        Assert.False(Directory.Exists(scratchDirectory));
    }

    [Fact]
    public void TryDelete_is_a_harmless_no_op_when_the_scratch_directory_does_not_exist()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        var exception = Record.Exception(() => AgentInvocationScratchDirectory.TryDelete(runId, attemptId));

        Assert.Null(exception);
    }

    [WindowsOnlyFact]
    public void TryDelete_never_recurses_through_a_reparse_point_scratch_directory_and_leaves_its_real_target_untouched()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        var realTargetDirectory = Path.Combine(_scratchWorkspace, "real-target");
        Directory.CreateDirectory(realTargetDirectory);
        var nestedRealDirectory = Path.Combine(realTargetDirectory, "nested");
        Directory.CreateDirectory(nestedRealDirectory);
        const string SentinelContent = "sentinel-scratch-directory-real-target-must-not-be-deleted";
        var sentinelPath = Path.Combine(realTargetDirectory, "sentinel.txt");
        File.WriteAllText(sentinelPath, SentinelContent);
        var nestedSentinelPath = Path.Combine(nestedRealDirectory, "nested-sentinel.txt");
        File.WriteAllText(nestedSentinelPath, SentinelContent);

        var scratchParent = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevalCopilot", "agent-scratch",
            runId.ToString());
        Directory.CreateDirectory(scratchParent);
        // The junction sits exactly at the leaf path EnsureExists/TryDelete construct for this
        // run/attempt pair — the scratch directory itself is the reparse point here.
        var junctionPath = Path.Combine(scratchParent, attemptId.ToString());

        var junctionCreation = TryCreateJunction(junctionPath, realTargetDirectory);
        try
        {
            // Directory junction creation needs no elevation on Windows (verified empirically for
            // this suite in PackageEntrypointResolverTests) — a failure here is a genuine
            // environmental problem worth failing loudly over, never a reason to silently treat
            // the security assertion below as satisfied.
            Assert.True(
                junctionCreation.Succeeded,
                $"Directory junction creation failed (exit code {junctionCreation.ExitCode}): {junctionCreation.StandardError}");

            AgentInvocationScratchDirectory.TryDelete(runId, attemptId);

            // Best-effort cleanup still happened: the junction link itself is gone...
            Assert.False(Directory.Exists(junctionPath));
            // ...but its real target, and everything nested inside it, were never touched, read,
            // or deleted.
            Assert.True(Directory.Exists(realTargetDirectory));
            Assert.True(Directory.Exists(nestedRealDirectory));
            Assert.True(File.Exists(sentinelPath));
            Assert.Equal(SentinelContent, File.ReadAllText(sentinelPath));
            Assert.True(File.Exists(nestedSentinelPath));
            Assert.Equal(SentinelContent, File.ReadAllText(nestedSentinelPath));
        }
        finally
        {
            if (Directory.Exists(junctionPath))
            {
                Directory.Delete(junctionPath);
            }

            if (Directory.Exists(scratchParent))
            {
                Directory.Delete(scratchParent, recursive: true);
            }
        }
    }

    [WindowsOnlyFact]
    public void TryDelete_never_recurses_through_a_reparse_point_ancestor_and_leaves_its_real_target_untouched()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        var realTargetDirectory = Path.Combine(_scratchWorkspace, "real-ancestor-target");
        Directory.CreateDirectory(realTargetDirectory);

        // The external target already contains the EXACT {attemptId} leaf directory ScratchPath
        // computes, with a sentinel file inside it — the precise location a recursive delete
        // through the junction would reach, not a sibling of it. A prior round's ancestor-junction
        // test elsewhere in this codebase planted its sentinel as a sibling of the generated
        // attempt directory and explicitly accepted that the attempt directory itself got deleted
        // through the junction, which disguised the bug this method now guards against as a
        // passing test.
        var realAttemptDirectory = Path.Combine(realTargetDirectory, attemptId.ToString());
        Directory.CreateDirectory(realAttemptDirectory);
        const string SentinelContent = "sentinel-scratch-directory-ancestor-must-not-be-touched";
        var sentinelPath = Path.Combine(realAttemptDirectory, "sentinel.txt");
        File.WriteAllText(sentinelPath, SentinelContent);

        var scratchRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevalCopilot", "agent-scratch");
        Directory.CreateDirectory(scratchRoot);
        // The junction sits at the *runId* segment — an intermediate ancestor of the leaf path
        // TryDelete computes (…\agent-scratch\{runId}\{attemptId}) — never at the leaf itself,
        // which the leaf-is-a-reparse-point test above already covers.
        var runJunctionPath = Path.Combine(scratchRoot, runId.ToString());

        var junctionCreation = TryCreateJunction(runJunctionPath, realTargetDirectory);
        try
        {
            Assert.True(
                junctionCreation.Succeeded,
                $"Directory junction creation failed (exit code {junctionCreation.ExitCode}): {junctionCreation.StandardError}");

            AgentInvocationScratchDirectory.TryDelete(runId, attemptId);

            // TryDelete never deletes anything through an ancestor reparse point: not the leaf
            // (reached only via the junction), and — unlike the leaf-is-a-reparse-point case,
            // where the link itself is removed non-recursively — not the ancestor junction link
            // either, because this method never touches any path but the leaf it was asked to
            // clean up. All three remain exactly as created.
            Assert.True(Directory.Exists(runJunctionPath));
            Assert.True(Directory.Exists(realTargetDirectory));
            Assert.True(Directory.Exists(realAttemptDirectory));
            Assert.True(File.Exists(sentinelPath));
            Assert.Equal(SentinelContent, File.ReadAllText(sentinelPath));
        }
        finally
        {
            if (Directory.Exists(runJunctionPath))
            {
                Directory.Delete(runJunctionPath);
            }
        }
    }

    [WindowsOnlyFact]
    public void EnsureExists_throws_and_creates_nothing_when_an_existing_ancestor_is_a_reparse_point()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        var realTargetDirectory = Path.Combine(_scratchWorkspace, "real-ensure-exists-ancestor-target");
        Directory.CreateDirectory(realTargetDirectory);

        var scratchRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevalCopilot", "agent-scratch");
        Directory.CreateDirectory(scratchRoot);
        var runJunctionPath = Path.Combine(scratchRoot, runId.ToString());

        var junctionCreation = TryCreateJunction(runJunctionPath, realTargetDirectory);
        try
        {
            Assert.True(
                junctionCreation.Succeeded,
                $"Directory junction creation failed (exit code {junctionCreation.ExitCode}): {junctionCreation.StandardError}");

            var exception = Record.Exception(() => AgentInvocationScratchDirectory.EnsureExists(runId, attemptId));

            Assert.IsType<IOException>(exception);
            // Refusing to create anything through the reparse-point ancestor means the attempt
            // leaf is never brought into existence inside the junction's real target, either.
            var realAttemptDirectory = Path.Combine(realTargetDirectory, attemptId.ToString());
            Assert.False(Directory.Exists(realAttemptDirectory));
        }
        finally
        {
            if (Directory.Exists(runJunctionPath))
            {
                Directory.Delete(runJunctionPath);
            }
        }
    }

    // The exact bypass a prior version of ValidateNoAncestorIsAReparsePoint allowed through: the
    // old loop started at the immediate parent and walked upward only while
    // `Directory.Exists(ancestor)` was true. With the runId directory absent (the ordinary case
    // for a brand-new attempt), that condition was false on the very first check, and the loop
    // exited immediately — never reaching the "agent-scratch" segment two levels up, even though
    // IT already existed as a reparse point. Uses the internal path-based overloads so this test
    // never has to replace or delete the real shared
    // `%LocalAppData%\DevalCopilot\agent-scratch` directory — a fully isolated fake root under
    // this test's own temp workspace stands in for it instead.
    [WindowsOnlyFact]
    public void EnsureExists_throws_and_creates_nothing_when_an_upper_ancestor_is_a_reparse_point_and_lower_segments_do_not_exist_yet()
    {
        var fakeScratchRoot = Path.Combine(_scratchWorkspace, "agent-scratch");
        var realTargetDirectory = Path.Combine(_scratchWorkspace, "real-upper-ancestor-target");
        Directory.CreateDirectory(realTargetDirectory);
        const string SentinelContent = "sentinel-upper-ancestor-must-not-be-touched";
        var sentinelPath = Path.Combine(realTargetDirectory, "sentinel.txt");
        File.WriteAllText(sentinelPath, SentinelContent);

        var junctionCreation = TryCreateJunction(fakeScratchRoot, realTargetDirectory);
        try
        {
            Assert.True(
                junctionCreation.Succeeded,
                $"Directory junction creation failed (exit code {junctionCreation.ExitCode}): {junctionCreation.StandardError}");

            var runId = Guid.NewGuid();
            var attemptId = Guid.NewGuid();
            var runDirectory = Path.Combine(fakeScratchRoot, runId.ToString());
            var leafPath = Path.Combine(runDirectory, attemptId.ToString());

            // Confirms the exact precondition the bug relied on: neither the run nor the attempt
            // directory exists anywhere yet — only the ancestor two levels up is the reparse point.
            Assert.False(Directory.Exists(runDirectory));
            Assert.False(Directory.Exists(leafPath));

            var exception = Record.Exception(() => AgentInvocationScratchDirectory.EnsureExists(leafPath));

            Assert.IsType<IOException>(exception);
            // No run or attempt directory was ever created through the junction, at any depth —
            // neither at the logical (junction-side) path nor inside the junction's real target.
            Assert.False(Directory.Exists(runDirectory));
            Assert.False(Directory.Exists(leafPath));
            Assert.False(Directory.Exists(Path.Combine(realTargetDirectory, runId.ToString())));
            // The sentinel and its containing target remain exactly as created.
            Assert.True(Directory.Exists(realTargetDirectory));
            Assert.True(File.Exists(sentinelPath));
            Assert.Equal(SentinelContent, File.ReadAllText(sentinelPath));
        }
        finally
        {
            // Removed before this class's own Dispose() recursively deletes _scratchWorkspace —
            // a non-recursive delete of just the junction link, never following it into the real
            // target (mirrors every sibling junction test's own cleanup).
            if (Directory.Exists(fakeScratchRoot))
            {
                Directory.Delete(fakeScratchRoot);
            }
        }
    }

    /// <summary>Never leaks either the junction or target path: only the process's own exit code
    /// and stderr text are surfaced to a failing assertion. Mirrors the identical helper already
    /// established in <c>PackageEntrypointResolverTests</c> and <c>CodexPlanningAdapterTests</c>.</summary>
    private static JunctionCreationResult TryCreateJunction(string junctionPath, string targetPath)
    {
        try
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
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            var succeeded = process.ExitCode == 0 && Directory.Exists(junctionPath);
            return new JunctionCreationResult(succeeded, process.ExitCode, standardError);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new JunctionCreationResult(false, -1, exception.GetType().Name);
        }
    }

    private readonly record struct JunctionCreationResult(bool Succeeded, int ExitCode, string StandardError);
}
