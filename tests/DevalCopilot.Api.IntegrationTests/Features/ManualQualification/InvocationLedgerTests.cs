using System.Diagnostics;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

public sealed class InvocationLedgerTests : QualificationTestBase
{
    [Fact]
    public void An_allowance_can_be_consumed_exactly_once()
    {
        Assert.False(Ledger.IsSpent());

        Assert.Equal(ConsumeOutcome.Consumed, Ledger.Consume(AllowanceRole.Planner, "s1"));
        Assert.Equal(ConsumeOutcome.AlreadyConsumed, Ledger.Consume(AllowanceRole.Planner, "s1"));
        Assert.Equal(ConsumeOutcome.AlreadyConsumed, Ledger.Consume(AllowanceRole.Planner, "s2"));
        Assert.True(Ledger.IsSpent());
    }

    [Fact]
    public void The_reviewer_allowance_needs_the_planner_allowance_of_the_same_session()
    {
        Assert.Equal(ConsumeOutcome.FirstStageNotHeld, Ledger.Consume(AllowanceRole.Reviewer, "s1"));
        Ledger.Consume(AllowanceRole.Planner, "s1");

        Assert.Equal(ConsumeOutcome.FirstStageNotHeld, Ledger.Consume(AllowanceRole.Reviewer, "s2"));
        Assert.Equal(ConsumeOutcome.Consumed, Ledger.Consume(AllowanceRole.Reviewer, "s1"));
        Assert.Equal(ConsumeOutcome.AlreadyConsumed, Ledger.Consume(AllowanceRole.Reviewer, "s1"));
    }

    [Fact]
    public void A_new_ledger_object_over_the_same_directory_cannot_reset_an_entry()
    {
        Ledger.Consume(AllowanceRole.Planner, "s1");

        var restarted = new InvocationLedger(LedgerDirectory, TimeProvider.System);

        Assert.True(restarted.IsConsumed(AllowanceRole.Planner));
        Assert.True(restarted.IsSpent());
        Assert.Equal(ConsumeOutcome.AlreadyConsumed, restarted.Consume(AllowanceRole.Planner, "s3"));
    }

    [Fact]
    public async Task Racing_consumers_produce_exactly_one_winner()
    {
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(index => Task.Run(() => Ledger.Consume(AllowanceRole.Planner, $"racer-{index}"))));

        Assert.Equal(1, outcomes.Count(outcome => outcome == ConsumeOutcome.Consumed));
        Assert.Equal(15, outcomes.Count(outcome => outcome == ConsumeOutcome.AlreadyConsumed));
    }

    [Fact]
    public void An_entry_that_cannot_be_written_durably_is_not_a_consumed_allowance()
    {
        File.WriteAllText(LedgerDirectory, "a file where the ledger directory should be");

        try
        {
            Assert.Equal(ConsumeOutcome.Unwritable, Ledger.Consume(AllowanceRole.Planner, "s1"));
        }
        finally
        {
            File.Delete(LedgerDirectory);
        }
    }

    [Fact]
    public void A_ledger_directory_that_is_an_alias_is_refused()
    {
        var target = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"devalcopilot-xunit-ledger-target-{Guid.NewGuid():N}")).FullName;
        using (var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{LedgerDirectory}\" \"{target}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
        {
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        try
        {
            Assert.Equal(ConsumeOutcome.Unwritable, Ledger.Consume(AllowanceRole.Planner, "s1"));
            Assert.Empty(Directory.GetFileSystemEntries(target));
        }
        finally
        {
            Directory.Delete(LedgerDirectory);
            Directory.Delete(target, recursive: true);
        }
    }

    [Fact]
    public void The_entry_records_only_the_authorization_session_role_and_time()
    {
        Ledger.Consume(AllowanceRole.Planner, "opaque-session");

        var text = File.ReadAllText(Path.Combine(LedgerDirectory, "planner.allowance"));

        Assert.Contains(InvocationLedger.Authorization, text, StringComparison.Ordinal);
        Assert.Contains("opaque-session", text, StringComparison.Ordinal);
        Assert.DoesNotContain(":" + Path.DirectorySeparatorChar, text, StringComparison.Ordinal);
    }
}
