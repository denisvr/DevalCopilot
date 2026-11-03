using System.Security.Cryptography;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>QP-R1 (correction 3): persistence is a typed, checked part of finalization in both lots. A summary that could not be
/// created exclusively, written, flushed and closed never lets an entry return success, never overwrites an existing file, never
/// retries, and leaves the printed facts, the spent allowances and every existing file exactly as they are.</summary>
public sealed class CheckedPersistenceTests : QualificationTestBase
{
    private static string? Confirmed(string name) =>
        name == QualificationOptIn.ConfirmationVariable ? QualificationOptIn.ConfirmationPhrase : null;

    /// <summary>Puts an owned directory at the destination file name while the summary is being printed (the reviewer's control).</summary>
    private sealed class InterferingWriter(Action<string> beforeWrite) : StringWriter
    {
        public override Task WriteLineAsync(string? value)
        {
            beforeWrite(value!);
            return base.WriteLineAsync(value);
        }
    }

    private InterferingWriter ObstructingWriter(string prefix) => new(text =>
    {
        using var document = JsonDocument.Parse(text);
        Directory.CreateDirectory(Path.Combine(LedgerDirectory, prefix + document.RootElement.GetProperty("session").GetString() + ".json"));
    });

    private async Task<int> RunLotBAsync(ScriptedEnvironment environment, TextWriter output, TextWriter error) =>
        await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny with { Outer = TimeSpan.FromSeconds(10) }, () => environment,
            output, error, CancellationToken.None);

    private async Task<int> RunLotAAsync(IReadOnlyList<Func<CancellationToken, Task<DevalCopilot.Application.Features.Processes.Ports.ProcessExecutionResult>>> script, TextWriter output, TextWriter error)
    {
        Directory.CreateDirectory(LedgerDirectory);
        var binary = Path.Combine(LedgerDirectory, "claude.exe");
        await File.WriteAllBytesAsync(binary, [1, 2, 3]);
        var digest = Convert.ToHexStringLower(SHA256.HashData([1, 2, 3]));
        return await StatusComparisonEntry.RunAsync(
            [StatusComparisonOptIn.Flag, StatusComparisonOptIn.ExecutableOption, binary, StatusComparisonOptIn.Sha256Option, digest],
            _ => StatusComparisonOptIn.ConfirmationPhrase,
            new Dictionary<string, string>(), () => new Dictionary<string, string>(),
            LedgerDirectory, Path.GetTempPath(), new StatusLimits(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10)),
            () => new ScriptedStatusAdapter([.. script]), output, error, CancellationToken.None);
    }

    private static Func<CancellationToken, Task<DevalCopilot.Application.Features.Processes.Ports.ProcessExecutionResult>>[] BothAuthenticated =>
        [ScriptedStatusAdapter.Authenticated(), ScriptedStatusAdapter.Authenticated()];

    // The reviewer's two controls --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Lot_b_an_obstructed_summary_destination_is_a_closed_attention_exit_never_success()
    {
        using var output = ObstructingWriter("summary-");
        using var error = new StringWriter();

        var exit = await RunLotBAsync(new ScriptedEnvironment(), output, error);

        Assert.Equal(SummaryPersistence.NotPersistedExitCode, exit);
        Assert.Contains("could not be persisted", error.ToString(), StringComparison.Ordinal);
        // The valid exchange facts were printed and the allowances stay spent: nothing is refunded or retried.
        using var printed = JsonDocument.Parse(output.ToString());
        Assert.Equal("Qualified", printed.RootElement.GetProperty("result").GetString());
        Assert.True(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.True(Ledger.IsConsumed(AllowanceRole.Reviewer));
        Assert.Empty(Directory.GetFiles(LedgerDirectory, "summary-*.json"));
        Assert.Single(Directory.GetDirectories(LedgerDirectory, "summary-*.json"));
    }

    [Fact]
    public async Task Lot_a_an_obstructed_summary_destination_is_a_closed_attention_exit_never_success()
    {
        using var output = ObstructingWriter("status-summary-");
        using var error = new StringWriter();

        var exit = await RunLotAAsync(BothAuthenticated, output, error);

        Assert.Equal(SummaryPersistence.NotPersistedExitCode, exit);
        Assert.Contains("could not be persisted", error.ToString(), StringComparison.Ordinal);
        using var printed = JsonDocument.Parse(output.ToString());
        Assert.Equal("BothAuthenticated", printed.RootElement.GetProperty("decision").GetString());
        Assert.Equal(2, Directory.GetFiles(LedgerDirectory, "admission-claude.status.*.start").Length);
        Assert.Empty(Directory.GetFiles(LedgerDirectory, "status-summary-*.json"));
    }

    // Semantics ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_more_specific_non_success_exit_is_kept_when_the_summary_is_not_persisted_either()
    {
        using var output = ObstructingWriter("summary-");
        using var error = new StringWriter();
        var environment = new ScriptedEnvironment { Targets = new TargetJudgement(false, [new TargetReport("codex", false, "TargetMissing", null, null)]) };

        var exit = await RunLotBAsync(environment, output, error);

        Assert.Equal(4, exit);
        Assert.Contains("could not be persisted", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_qualified_result_that_needs_attention_becomes_the_not_persisted_exit_when_unsaved()
    {
        using var output = ObstructingWriter("summary-");
        using var error = new StringWriter();
        var environment = new ScriptedEnvironment { Cleanup = () => CleanupReport.Preserved("Attention") };

        var exit = await RunLotBAsync(environment, output, error);

        Assert.Equal(SummaryPersistence.NotPersistedExitCode, exit);
    }

    [Fact]
    public async Task An_existing_summary_file_is_never_overwritten_and_the_entry_never_retries()
    {
        Directory.CreateDirectory(LedgerDirectory);
        var sessionFiles = new List<string>();
        using var output = new InterferingWriter(text =>
        {
            using var document = JsonDocument.Parse(text);
            var path = Path.Combine(LedgerDirectory, "summary-" + document.RootElement.GetProperty("session").GetString() + ".json");
            File.WriteAllText(path, "earlier evidence");
            sessionFiles.Add(path);
        });
        using var error = new StringWriter();

        var exit = await RunLotBAsync(new ScriptedEnvironment(), output, error);

        Assert.Equal(SummaryPersistence.NotPersistedExitCode, exit);
        Assert.Equal("earlier evidence", File.ReadAllText(Assert.Single(sessionFiles)));
        Assert.Single(Directory.GetFiles(LedgerDirectory, "summary-*.json"));
    }

    [Fact]
    public async Task A_persisted_summary_still_returns_the_normal_exit_and_keeps_exactly_the_printed_text()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await RunLotBAsync(new ScriptedEnvironment(), output, error);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(output.ToString().Trim(), File.ReadAllText(Assert.Single(Directory.GetFiles(LedgerDirectory, "summary-*.json"))).Trim());
    }

    [Fact]
    public async Task Lot_a_keeps_its_non_success_exit_and_a_persisted_summary_still_returns_zero()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await RunLotAAsync(BothAuthenticated, output, error);

        Assert.Equal(0, exit);
        Assert.Equal(output.ToString().Trim(), File.ReadAllText(Assert.Single(Directory.GetFiles(LedgerDirectory, "status-summary-*.json"))).Trim());
    }

    // The typed persistence helper -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_helper_creates_exclusively_writes_exactly_and_reports_every_failure_as_false()
    {
        Directory.CreateDirectory(LedgerDirectory);

        Assert.True(await SummaryPersistence.SaveAsync(LedgerDirectory, "a.json", "{\"x\":1}"));
        Assert.Equal("{\"x\":1}", File.ReadAllText(Path.Combine(LedgerDirectory, "a.json")));
        Assert.False(await SummaryPersistence.SaveAsync(LedgerDirectory, "a.json", "{\"x\":2}"));
        Assert.Equal("{\"x\":1}", File.ReadAllText(Path.Combine(LedgerDirectory, "a.json")));
        Directory.CreateDirectory(Path.Combine(LedgerDirectory, "b.json"));
        Assert.False(await SummaryPersistence.SaveAsync(LedgerDirectory, "b.json", "{}"));
        Assert.False(await SummaryPersistence.SaveAsync(Path.Combine(LedgerDirectory, "a.json", "child"), "c.json", "{}"));
        Assert.False(await SummaryPersistence.SaveAsync(LedgerDirectory, "bad|name?.json", "{}"));
    }

    [Theory]
    [InlineData(0, true, 0)]
    [InlineData(0, false, 9)]
    [InlineData(6, true, 6)]
    [InlineData(6, false, 9)]
    [InlineData(5, false, 5)]
    [InlineData(4, false, 4)]
    [InlineData(3, false, 3)]
    [InlineData(8, false, 8)]
    public void Only_success_and_attention_exits_are_replaced_when_the_summary_was_not_persisted(int reported, bool persisted, int expected)
    {
        Assert.Equal(expected, SummaryPersistence.ExitCode(reported, persisted));
    }
}
