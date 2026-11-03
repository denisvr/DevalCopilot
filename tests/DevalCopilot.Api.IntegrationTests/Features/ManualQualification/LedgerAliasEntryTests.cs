using System.Diagnostics;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>A ledger destination that is itself a junction, symbolic link or other reparse point is judged from its own attributes and
/// never followed: the entry refuses it before it reads, enumerates, consumes or saves anything, an alias that appears later gets no
/// read, slot, allowance or summary, and the expiry verdict is subject to the same decision. Every read of the destination runs in the
/// bounded worker or an emission, never on the deadline waiter.</summary>
public sealed class LedgerAliasEntryTests : QualificationTestBase
{
    private static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(3);

    private readonly string _work = Path.Combine(Path.GetTempPath(), $"devalcopilot-xunit-alias-{Guid.NewGuid():N}");

    public override void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }

        base.Dispose();
    }

    private static string? Confirmed(string name) =>
        name == QualificationOptIn.ConfirmationVariable ? QualificationOptIn.ConfirmationPhrase : null;

    private static void Junction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void The_attribute_check_is_false_for_a_missing_destination_and_a_real_directory_and_true_for_an_alias_even_a_dangling_one()
    {
        var target = Directory.CreateDirectory(Path.Combine(_work, "target")).FullName;
        var link = Path.Combine(_work, "link");
        Junction(link, target);

        try
        {
            Assert.False(InvocationLedger.IsAliasedDestination(Path.Combine(_work, "missing")));
            Assert.False(InvocationLedger.IsAliasedDestination(target));
            Assert.True(InvocationLedger.IsAliasedDestination(link));
            Assert.True(InvocationLedger.IsAliasedDestination(link + Path.DirectorySeparatorChar));
            Directory.Delete(target);
            Assert.True(InvocationLedger.IsAliasedDestination(link), "a dangling alias is still an alias");
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task A_junction_at_the_ledger_destination_refuses_before_any_environment_read_slot_or_save()
    {
        var target = Directory.CreateDirectory(Path.Combine(_work, "target")).FullName;
        var link = Path.Combine(_work, "link");
        Junction(link, target);
        var ledger = new InvocationLedger(link, TimeProvider.System);
        var created = 0;
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            var exitCode = await LauncherEntry.RunAsync(
                [QualificationOptIn.Flag], Confirmed, ledger, Tiny, () => { created++; return new ScriptedEnvironment(); },
                output, error, CancellationToken.None);

            Assert.Equal(LauncherEntry.RefusedExitCode, exitCode);
            Assert.Equal($"Refused: {LauncherEntry.LedgerAliasedCode}.{Environment.NewLine}", error.ToString());
            Assert.Equal(string.Empty, output.ToString());
            Assert.Equal(0, created);
            Assert.Empty(Directory.GetFileSystemEntries(target));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task A_destination_that_becomes_an_alias_after_the_entry_check_gets_no_read_slot_allowance_or_summary()
    {
        var reads = 0;
        var environment = new ScriptedEnvironment();
        using var output = new StringWriter();
        using var error = new StringWriter();

        // The first read is the entry's own, the second the session's before it enumerates anything, the third the summary save's.
        var exitCode = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny, () => environment, output, error, CancellationToken.None,
            _ => Interlocked.Increment(ref reads) >= 2);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(3, reads);
        Assert.All(environment.Calls, call => Assert.Equal("dispose", call));
        using var summary = JsonDocument.Parse(output.ToString());
        Assert.Equal("Blocked", summary.RootElement.GetProperty("result").GetString());
        Assert.Equal(LauncherEntry.LedgerAliasedCode, summary.RootElement.GetProperty("code").GetString());
        Assert.Contains("could not be persisted", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(LedgerDirectory), "no allowance, slot or summary was created");
    }

    [Fact]
    public async Task An_alias_that_appears_before_the_expiry_verdict_is_saved_gets_no_summary()
    {
        var aliased = false;
        var environment = new ScriptedEnvironment
        {
            OnSubmit = async (_, _) =>
            {
                Volatile.Write(ref aliased, true);
                await Task.Delay(TimeSpan.FromSeconds(3));
                return SubmissionResult.Accepted(Guid.NewGuid());
            },
        };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny with { Overall = TimeSpan.FromSeconds(3), Outer = TimeSpan.FromMilliseconds(400) },
            () => environment, output, error, CancellationToken.None, _ => Volatile.Read(ref aliased));

        Assert.Equal(LauncherEntry.OuterDeadlineExitCode, exitCode);
        // Both emissions are bounded by the reserve and may complete after the return under load, so they are judged after the wait.
        await Task.Delay(500);
        using var verdict = JsonDocument.Parse(output.ToString());
        Assert.Equal("OuterDeadlineExceeded", verdict.RootElement.GetProperty("code").GetString());
        Assert.Empty(Directory.GetFiles(LedgerDirectory, "summary-*.json"));
    }

    [Fact]
    public async Task A_held_metadata_read_cannot_keep_the_entry_past_its_bound_and_a_late_release_starts_nothing()
    {
        using var release = new ManualResetEventSlim();
        var created = 0;
        using var output = new StringWriter();
        using var error = new StringWriter();
        var clock = Stopwatch.StartNew();

        var exitCode = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny with { Outer = TimeSpan.FromMilliseconds(300) },
            () => { Interlocked.Increment(ref created); return new ScriptedEnvironment(); }, output, error, CancellationToken.None,
            _ =>
            {
                release.Wait();
                return false;
            });

        Assert.Equal(LauncherEntry.OuterDeadlineExitCode, exitCode);
        Assert.True(clock.Elapsed < Elapsed, "the entry returned only after the held read");
        release.Set();
        await Task.Delay(500);

        Assert.Equal(0, created);
        Assert.False(Ledger.IsSpent());
    }
}
