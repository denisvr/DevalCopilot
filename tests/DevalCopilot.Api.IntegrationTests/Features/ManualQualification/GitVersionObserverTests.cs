using System.Diagnostics;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>QP-R3 (correction 4): the REAL <see cref="ProcessGitVersionObserver"/> against owned executable doubles (copies of the
/// deterministic process-execution fixture driven by a script file beside the copy): one monotonic deadline for exit and both
/// streams, byte caps on both streams, required completion of both, closed outcomes, no raw retention and honest unproven
/// termination. No real Git or provider is started.</summary>
public sealed class GitVersionObserverTests : QualificationTestBase
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(2);

    private readonly List<string> _descendantFiles = [];

    private string Scratch => Path.Combine(LedgerDirectory, "scratch");

    private string Executable => Path.Combine(LedgerDirectory, "git-double", "git.exe");

    /// <summary>Writes an owned copy of the fixture executable and the script that tells it what to do for <c>--version</c>.</summary>
    private string Install(string script)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Executable)!);
        Directory.CreateDirectory(Scratch);
        // Copied once per test: a descendant of an earlier observation in the same test may still hold the files open.
        foreach (var name in new[] { "ProcessExecutionFixture.dll", "ProcessExecutionFixture.runtimeconfig.json", "ProcessExecutionFixture.deps.json" })
        {
            var target = Path.Combine(Path.GetDirectoryName(Executable)!, name);
            if (!File.Exists(target))
            {
                File.Copy(Path.Combine(AppContext.BaseDirectory, name), target);
            }
        }

        if (!File.Exists(Executable))
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe"), Executable);
        }

        File.WriteAllText(Path.ChangeExtension(Executable, ".observation"), script);
        return Executable;
    }

    private (GitVersionObservation Observation, TimeSpan Elapsed) Observe(string script, TimeSpan? bound = null)
    {
        var path = Install(script);
        var clock = Stopwatch.StartNew();
        var observation = new ProcessGitVersionObserver().Observe(path, Scratch, bound ?? Bound);
        return (observation, clock.Elapsed);
    }

    private string DescendantFile()
    {
        var name = "descendant-" + Guid.NewGuid().ToString("N") + ".pid";
        _descendantFiles.Add(Path.Combine(Scratch, name));
        return name;
    }

    public override void Dispose()
    {
        foreach (var file in _descendantFiles.Where(File.Exists))
        {
            try
            {
                using var descendant = Process.GetProcessById(int.Parse(File.ReadAllText(file)));
                descendant.Kill(entireProcessTree: true);
                descendant.WaitForExit(3000);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
        }

        base.Dispose();
    }

    // Normal -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_normal_observation_is_complete_and_compatible()
    {
        var (observation, _) = Observe("normal");

        Assert.True(observation.Exited);
        Assert.Equal(0, observation.ExitCode);
        Assert.False(observation.TimedOut);
        Assert.False(observation.Truncated);
        Assert.True(observation.IoComplete);
        Assert.False(observation.TerminationUnproven);
        Assert.StartsWith("git version 2.50.0", observation.Output, StringComparison.Ordinal);
        Assert.Null(GitVersionGate.Evaluate(observation));
    }

    [Fact]
    public void An_exit_that_arrives_late_but_inside_the_deadline_is_still_a_normal_observation()
    {
        var (observation, elapsed) = Observe("delay-ms 500", TimeSpan.FromSeconds(5));

        Assert.Null(GitVersionGate.Evaluate(observation));
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(400));
    }

    [Fact]
    public void A_non_zero_exit_is_a_failed_observation()
    {
        var (observation, _) = Observe("exit-code 3");

        Assert.Equal(3, observation.ExitCode);
        Assert.Equal("GitVersionObservationFailed", GitVersionGate.Evaluate(observation));
    }

    [Fact]
    public void A_file_that_cannot_be_started_is_a_failed_observation_with_nothing_run()
    {
        Directory.CreateDirectory(Scratch);

        var observation = new ProcessGitVersionObserver().Observe(Path.Combine(LedgerDirectory, "absent", "git.exe"), Scratch, Bound);

        Assert.False(observation.Exited);
        // No process was started or returned, so no stream was read to its end: the observation claims no completed I/O.
        Assert.False(observation.IoComplete);
        Assert.False(observation.TimedOut);
        Assert.False(observation.TerminationUnproven);
        Assert.Equal("GitVersionObservationFailed", GitVersionGate.Evaluate(observation));
    }

    // Byte caps on both streams ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(4096)]
    [InlineData(1_000_000)]
    public void An_oversized_standard_output_is_refused_as_incomplete_and_nothing_of_it_is_kept(int extraBytes)
    {
        var (observation, elapsed) = Observe($"stdout-bytes {extraBytes}");

        Assert.True(observation.Truncated);
        Assert.Equal(string.Empty, observation.Output);
        Assert.Equal("GitVersionIncomplete", GitVersionGate.Evaluate(observation));
        Assert.True(elapsed < Bound + TimeSpan.FromSeconds(1), "the observation kept reading an oversized stream");
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(1_000_000)]
    public void An_oversized_standard_error_is_refused_even_beside_a_valid_version_line(int bytes)
    {
        // The reviewer's control: a valid version plus 4,096 standard error bytes was accepted before.
        var (observation, elapsed) = Observe($"stderr-bytes {bytes}");

        Assert.True(observation.Truncated);
        Assert.Equal(string.Empty, observation.Output);
        Assert.Equal("GitVersionIncomplete", GitVersionGate.Evaluate(observation));
        Assert.True(elapsed < Bound + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void The_caps_are_exact_in_bytes_on_each_stream()
    {
        // "git version 2.50.0\n" is 19 bytes: 493 more reach exactly 512 on standard output, 494 reach 513.
        var atCapOut = Observe("stdout-bytes 493").Observation;
        var overCapOut = Observe("stdout-bytes 494").Observation;
        var atCapError = Observe("stderr-bytes 512").Observation;
        var overCapError = Observe("stderr-bytes 513").Observation;

        Assert.False(atCapOut.Truncated);
        Assert.True(overCapOut.Truncated);
        Assert.False(atCapError.Truncated);
        Assert.True(overCapError.Truncated);
        // Within the cap the bytes are complete but not a version line (stdout), or tolerated and discarded (stderr).
        Assert.Equal("GitVersionUnknown", GitVersionGate.Evaluate(atCapOut));
        Assert.Null(GitVersionGate.Evaluate(atCapError));
        Assert.Equal(GitVersionGate.MaximumOutputBytes, 512);
    }

    // One monotonic deadline ----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_delayed_exit_ends_at_the_one_deadline_with_the_child_stopped_and_nothing_kept()
    {
        var (observation, elapsed) = Observe("delay-ms 5000", TimeSpan.FromMilliseconds(800));

        Assert.True(observation.TimedOut);
        Assert.False(observation.Exited);
        Assert.False(observation.TerminationUnproven);
        Assert.Equal(string.Empty, observation.Output);
        Assert.Equal("GitVersionObservationFailed", GitVersionGate.Evaluate(observation));
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(700), "the observation ended before its deadline");
        Assert.True(elapsed < TimeSpan.FromMilliseconds(800) + TimeSpan.FromSeconds(1), "the observation waited past its one deadline");
    }

    // Independently unfinished pipes ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("stdout")]
    [InlineData("stderr")]
    public void A_stream_that_never_ends_is_refused_even_though_the_process_exited_and_termination_is_unproven(string held)
    {
        var (observation, elapsed) = Observe($"hold {held} {DescendantFile()} 30000", TimeSpan.FromMilliseconds(800));

        Assert.True(observation.Exited);
        Assert.Equal(0, observation.ExitCode);
        Assert.False(observation.IoComplete);
        Assert.True(observation.TerminationUnproven);
        Assert.Equal(string.Empty, observation.Output);
        Assert.Equal("GitVersionTerminationUnproven", GitVersionGate.Evaluate(observation));
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(700));
        Assert.True(elapsed < TimeSpan.FromMilliseconds(800) + TimeSpan.FromSeconds(1), "the unfinished pipe got a second full wait");
    }

    [Fact]
    public void Standard_output_and_standard_error_are_required_independently()
    {
        var holdOut = Observe($"hold stdout {DescendantFile()} 30000", TimeSpan.FromMilliseconds(600)).Observation;
        var holdError = Observe($"hold stderr {DescendantFile()} 30000", TimeSpan.FromMilliseconds(600)).Observation;

        Assert.False(holdOut.IoComplete);
        Assert.False(holdError.IoComplete);
        Assert.NotNull(GitVersionGate.Evaluate(holdOut));
        Assert.NotNull(GitVersionGate.Evaluate(holdError));
    }

    // Correction 5: expiry is terminal --------------------------------------------------------------------------------------------

    private GitVersionObservation ObserveWith(ProcessGitVersionObserver observer, string script, TimeSpan bound)
    {
        var path = Install(script);
        return observer.Observe(path, Scratch, bound);
    }

    [Fact]
    public void A_wait_that_timed_out_refuses_even_when_the_process_and_both_readers_finished_before_the_decision()
    {
        // Deterministic: the wait seam lets every task complete first and then reports that the time elapsed, which is exactly the
        // timeout-then-completion interleaving. The observation must be refused, not accepted from the status reads that follow.
        var observer = new ProcessGitVersionObserver((tasks, _) =>
        {
            tasks[0].Wait(TimeSpan.FromSeconds(5));
            Assert.True(tasks[0].IsCompletedSuccessfully);
            return -1;
        });

        var observation = ObserveWith(observer, "normal", TimeSpan.FromSeconds(5));

        Assert.True(observation.TimedOut);
        Assert.True(observation.Exited);
        Assert.Equal(0, observation.ExitCode);
        Assert.True(observation.IoComplete);
        Assert.Equal(string.Empty, observation.Output);
        Assert.Equal("GitVersionObservationFailed", GitVersionGate.Evaluate(observation));
    }

    [Fact]
    public void A_success_index_returned_after_the_absolute_deadline_is_refused_as_expired()
    {
        // Deterministic: the seam receives the relative time left, sleeps past it (a caller descheduled between computing the timeout
        // and waiting), lets everything finish and still answers "completed". The absolute monotonic deadline is no longer open.
        var observer = new ProcessGitVersionObserver((tasks, remaining) =>
        {
            Thread.Sleep(remaining + TimeSpan.FromMilliseconds(50));
            Assert.True(tasks[0].Wait(TimeSpan.FromSeconds(5)));
            return 0;
        });

        var observation = ObserveWith(observer, "normal", TimeSpan.FromSeconds(1));

        Assert.True(observation.TimedOut);
        Assert.True(observation.Exited);
        Assert.Equal(0, observation.ExitCode);
        Assert.True(observation.IoComplete);
        Assert.Equal(string.Empty, observation.Output);
        Assert.Equal("GitVersionObservationFailed", GitVersionGate.Evaluate(observation));
    }

    [Fact]
    public void A_success_index_returned_inside_the_deadline_is_still_accepted()
    {
        var observer = new ProcessGitVersionObserver((tasks, _) =>
        {
            Assert.True(tasks[0].Wait(TimeSpan.FromSeconds(5)));
            return 0;
        });

        var observation = ObserveWith(observer, "normal", TimeSpan.FromSeconds(20));

        Assert.False(observation.TimedOut);
        Assert.Null(GitVersionGate.Evaluate(observation));
    }

    [Fact]
    public void An_observation_that_has_no_time_left_to_start_its_wait_is_expired_whatever_the_process_then_does()
    {
        var waitCalled = false;
        var observer = new ProcessGitVersionObserver((_, _) =>
        {
            waitCalled = true;
            return 0;
        });

        var observation = ObserveWith(observer, "normal", TimeSpan.Zero);

        Assert.False(waitCalled);
        Assert.True(observation.TimedOut);
        Assert.Equal(string.Empty, observation.Output);
        Assert.NotNull(GitVersionGate.Evaluate(observation));
    }

    [Fact]
    public void A_wait_that_established_completion_accepts_the_observation_and_a_failed_read_alone_never_does()
    {
        var accepting = new ProcessGitVersionObserver((tasks, _) =>
        {
            tasks[0].Wait(TimeSpan.FromSeconds(5));
            return 0;
        });

        var accepted = ObserveWith(accepting, "normal", TimeSpan.FromSeconds(5));

        Assert.False(accepted.TimedOut);
        Assert.Null(GitVersionGate.Evaluate(accepted));
    }

    [Fact]
    public void An_oversize_signal_that_wins_the_wait_refuses_without_expiry()
    {
        var observer = new ProcessGitVersionObserver((tasks, _) =>
        {
            tasks[1].Wait(TimeSpan.FromSeconds(5));
            return 1;
        });

        var observation = ObserveWith(observer, "stdout-bytes 4096", TimeSpan.FromSeconds(5));

        Assert.True(observation.Truncated);
        Assert.False(observation.TimedOut);
        Assert.Equal(string.Empty, observation.Output);
    }

    // Correction 5: overflow and unproven termination are independent facts -------------------------------------------------------

    [Fact]
    public void Overflow_on_one_stream_does_not_hide_a_descendant_still_holding_the_other_after_the_root_exited()
    {
        // The reviewer's control: the root exits, an owned descendant holding BOTH pipes writes 4 KiB to standard error and keeps
        // holding standard output.
        var (observation, elapsed) = Observe($"overflow-held {DescendantFile()} 4096 30000", TimeSpan.FromSeconds(3));

        Assert.True(observation.Exited);
        Assert.Equal(0, observation.ExitCode);
        Assert.True(observation.Truncated);
        Assert.False(observation.IoComplete);
        Assert.True(observation.TerminationUnproven);
        Assert.False(observation.TimedOut);
        Assert.Equal(string.Empty, observation.Output);
        Assert.Equal("GitVersionTerminationUnproven", GitVersionGate.Evaluate(observation));
        Assert.True(elapsed < TimeSpan.FromSeconds(3), "the overflow did not stop the observation before its deadline");
        var recorded = Path.Combine(Scratch, Directory.GetFiles(Scratch, "descendant-*.pid").Select(Path.GetFileName).Single()!);
        using var descendant = Process.GetProcessById(int.Parse(File.ReadAllText(recorded)));
        Assert.False(descendant.HasExited);
    }

    [Fact]
    public void A_plain_overflow_with_the_root_gone_and_nothing_held_stays_truncated_without_unproven_termination()
    {
        var (observation, _) = Observe("stdout-bytes 4096");

        Assert.True(observation.Truncated);
        Assert.False(observation.TerminationUnproven);
        Assert.Equal("GitVersionIncomplete", GitVersionGate.Evaluate(observation));
    }

    [Fact]
    public void Io_completion_means_both_readers_reached_the_end_and_is_independent_of_the_overflow_fact()
    {
        Assert.True(Observe("normal").Observation.IoComplete);

        // An overflow whose streams then ended (the extra bytes were read and discarded, never stored) is truncated AND ended.
        var overflowed = Observe("stdout-bytes 4096").Observation;
        Assert.True(overflowed.Truncated);
        Assert.True(overflowed.IoComplete);

        // A stream that never ends leaves it false, whichever one it is.
        Assert.False(Observe($"hold stdout {DescendantFile()} 30000", TimeSpan.FromMilliseconds(500)).Observation.IoComplete);
        Assert.False(Observe($"hold stderr {DescendantFile()} 30000", TimeSpan.FromMilliseconds(500)).Observation.IoComplete);
    }

    // The gate maps the new outcomes without a second look --------------------------------------------------------------------------

    [Fact]
    public void An_incomplete_io_or_unproven_termination_observation_is_a_closed_refusal_whatever_it_printed()
    {
        Assert.Equal("GitVersionIncomplete", GitVersionGate.Evaluate(new GitVersionObservation(true, 0, false, false, "git version 2.50.0", IoComplete: false)));
        Assert.Equal("GitVersionTerminationUnproven", GitVersionGate.Evaluate(new GitVersionObservation(true, 0, false, false, "git version 2.50.0", TerminationUnproven: true)));
        Assert.Equal("GitVersionTerminationUnproven", GitVersionGate.Evaluate(new GitVersionObservation(false, null, true, false, string.Empty, TerminationUnproven: true)));
        Assert.Equal("GitVersionIncomplete", GitVersionGate.Evaluate(new GitVersionObservation(false, null, false, true, string.Empty)));
    }
}
