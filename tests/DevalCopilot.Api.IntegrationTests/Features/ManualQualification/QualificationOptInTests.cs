using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

public sealed class QualificationOptInTests : QualificationTestBase
{
    private static string? Confirmed(string name) =>
        name == QualificationOptIn.ConfirmationVariable ? QualificationOptIn.ConfirmationPhrase : null;

    [Fact]
    public void The_flag_and_the_exact_phrase_together_are_the_only_opt_in()
    {
        Assert.True(QualificationOptIn.IsGranted([QualificationOptIn.Flag], Confirmed));
    }

    [Theory]
    [InlineData(false, "one-codex-planner-and-one-claude-critical-review")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "yes")]
    [InlineData(true, "ONE-CODEX-PLANNER-AND-ONE-CLAUDE-CRITICAL-REVIEW")]
    public void A_missing_or_inexact_half_of_the_opt_in_is_refused(bool flag, string? phrase)
    {
        string[] arguments = flag ? [QualificationOptIn.Flag] : [];

        Assert.False(QualificationOptIn.IsGranted(arguments, name => name == QualificationOptIn.ConfirmationVariable ? phrase : null));
    }

    [Fact]
    public void Extra_or_different_arguments_are_refused()
    {
        Assert.False(QualificationOptIn.IsGranted([QualificationOptIn.Flag, "--again"], Confirmed));
        Assert.False(QualificationOptIn.IsGranted(["--run-real-provider-sessions"], Confirmed));
        Assert.False(QualificationOptIn.IsGranted([QualificationOptIn.Flag.ToUpperInvariant()], Confirmed));
    }

    [Fact]
    public async Task A_refused_launch_creates_no_environment_no_directory_and_no_ledger_entry()
    {
        var created = 0;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await LauncherEntry.RunAsync(
            [], _ => null, Ledger, Tiny, () => { created++; return new ScriptedEnvironment(); }, output, error, CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, created);
        Assert.False(Directory.Exists(LedgerDirectory));
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains(QualificationOptIn.Flag, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unexpected_failure_reports_only_the_exception_type_never_a_stack_trace_or_path()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag],
            Confirmed,
            Ledger,
            Tiny,
            () => throw new InvalidOperationException("C:/Users/someone/secret-project"),
            output,
            error,
            CancellationToken.None);

        Assert.Equal(LauncherEntry.UnexpectedFailureExitCode, exitCode);
        Assert.Equal("The launcher stopped unexpectedly (InvalidOperationException)." + Environment.NewLine, error.ToString());
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task A_granted_launch_prints_only_the_safe_summary_and_keeps_a_copy_beside_the_ledger()
    {
        var environment = new ScriptedEnvironment();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny, () => environment, output, error, CancellationToken.None);

        Assert.Equal(0, exitCode);
        using var summary = JsonDocument.Parse(output.ToString());
        Assert.Equal("Qualified", summary.RootElement.GetProperty("result").GetString());
        var saved = Assert.Single(Directory.GetFiles(LedgerDirectory, "summary-*.json"));
        Assert.Equal(output.ToString().Trim(), File.ReadAllText(saved).Trim());
    }
}
