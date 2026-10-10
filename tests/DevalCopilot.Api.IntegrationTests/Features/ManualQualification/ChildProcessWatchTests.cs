using System.Diagnostics;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>The child-shutdown proof against real processes. <c>ping.exe</c> stands in for any helper a provider or the host might
/// leave behind: it needs no install, it is not named like a provider, and a long count keeps it alive until the test kills it.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ChildProcessWatchTests : IDisposable
{
    private static readonly string[] Names = ["ping.exe"];

    private readonly List<int> _started = [];

    public void Dispose()
    {
        foreach (var id in _started)
        {
            Kill(id);
        }

        GC.SuppressFinalize(this);
    }

    private static void Kill(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    private Process Start(string file, string arguments)
    {
        var process = Process.Start(new ProcessStartInfo(file, arguments)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        _started.Add(process.Id);
        return process;
    }

    private Process Ping() => Start("ping.exe", "-n 60 127.0.0.1");

    [Fact]
    public async Task A_process_that_already_ran_before_the_watch_began_is_ignored()
    {
        using var earlier = Ping();
        var watch = ChildProcessWatch.Begin();

        Assert.Empty(watch.Leftovers());
        Assert.True((await watch.ProveAsync(TimeSpan.Zero)).Proven);
    }

    [Fact]
    public async Task A_descendant_is_a_leftover_whatever_its_executable_name_until_it_is_stopped()
    {
        var watch = ChildProcessWatch.Begin();
        using var child = Ping();

        Assert.Contains(child.Id, watch.Leftovers().Select(entry => entry.Id));
        var unproven = await watch.ProveAsync(TimeSpan.FromMilliseconds(300));
        Assert.False(unproven.Proven);
        Assert.Equal("LeftoverAlive", unproven.Reason);

        child.Kill(entireProcessTree: true);
        child.WaitForExit();
        Assert.Equal(ChildProof.Stopped, await watch.ProveAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task A_helper_launched_through_a_shell_is_a_leftover_even_after_its_shell_ended()
    {
        var watch = ChildProcessWatch.Begin();
        watch.StartSampling(TimeSpan.FromMilliseconds(50));
        using var shell = Start("cmd.exe", "/c ping.exe -n 60 127.0.0.1");
        await Task.Delay(1500);
        shell.Kill();
        shell.WaitForExit();

        var proof = await watch.ProveAsync(TimeSpan.FromMilliseconds(300));

        Assert.False(proof.Proven);
        Assert.Equal("LeftoverAlive", proof.Reason);
        var leftover = Assert.Single(watch.Leftovers(), entry => entry.ExecutableName.Equals("ping.exe", StringComparison.OrdinalIgnoreCase));
        Kill(leftover.Id);
    }

    [Fact]
    public async Task A_provider_named_orphan_that_was_never_seen_under_a_live_parent_is_still_a_leftover()
    {
        var watch = ChildProcessWatch.Begin(orphanNames: Names);
        using (var launcher = Start("cmd.exe", "/c start \"\" /b ping.exe -n 60 127.0.0.1"))
        {
            launcher.WaitForExit();
        }

        var leftover = Assert.Single(await Eventually(watch));
        Assert.NotNull(Process.GetProcessById(leftover.Id));
        Kill(leftover.Id);
        Assert.True((await watch.ProveAsync(TimeSpan.FromSeconds(5))).Proven);
    }

    [Fact]
    public async Task A_new_process_under_a_pre_existing_parent_with_a_readable_identity_is_not_ours_and_is_left_alone()
    {
        using var preexisting = Start("cmd.exe", "/c ping.exe -n 3 127.0.0.1 >nul & ping.exe -n 60 127.0.0.1 >nul");
        var watch = ChildProcessWatch.Begin(orphanNames: Names);
        var known = new ProcessTable().Read().Select(entry => entry.Id).ToHashSet();

        ProcessEntry? unrelated = null;
        for (var attempt = 0; attempt < 100 && unrelated is null; attempt++)
        {
            unrelated = new ProcessTable().Read().FirstOrDefault(entry =>
                entry.ParentId == preexisting.Id
                && !known.Contains(entry.Id)
                && entry.ExecutableName.Equals("ping.exe", StringComparison.OrdinalIgnoreCase));
            await Task.Delay(100);
        }

        Assert.NotNull(unrelated);
        _started.Add(unrelated.Id);
        Assert.NotNull(unrelated.StartedUtc);
        watch.Sample();

        Assert.Empty(watch.Leftovers());
        Assert.True((await watch.ProveAsync(TimeSpan.Zero)).Proven);
        Assert.NotNull(Process.GetProcessById(unrelated.Id));
    }

    [Fact]
    public void The_native_table_reports_this_process_with_its_parent_and_creation_time()
    {
        var table = new ProcessTable().Read();

        var self = table.Single(entry => entry.Id == Environment.ProcessId);

        Assert.True(table.Count > 10);
        Assert.NotNull(self.StartedUtc);
        Assert.Contains(table, entry => entry.Id == self.ParentId);
    }

    private static async Task<IReadOnlyList<ProcessEntry>> Eventually(ChildProcessWatch watch)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var leftovers = watch.Leftovers();
            if (leftovers.Count > 0)
            {
                return leftovers;
            }

            await Task.Delay(100);
        }

        return [];
    }
}
