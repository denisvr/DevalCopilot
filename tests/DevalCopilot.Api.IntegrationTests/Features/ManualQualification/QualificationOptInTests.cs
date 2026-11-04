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
    [InlineData(false, QualificationOptIn.ConfirmationPhrase)]
    [InlineData(true, "one-codex-planner-and-one-claude-critical-review")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "yes")]
    [InlineData(true, "QUALIFICATION-PROFILE-01-ONE-CODEX-PLANNER-AND-ONE-CLAUDE-CRITICAL-REVIEW")]
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
        Assert.False(QualificationOptIn.IsGranted(["--run-real-provider-session"], Confirmed));
        Assert.False(QualificationOptIn.IsGranted([QualificationOptIn.Flag.ToUpperInvariant()], Confirmed));
    }

    // The retired phrases, kept here only to prove that they are refused: the first lot B entry's allowance and process slots were
    // consumed, the context-bound and Windows-root entries' authorizations were each spent by one native Codex Planner exit, and the
    // proposal-guidance entry's two allowances were used (one accepted Proposal, then a reviewer exit with no review).
    private const string RetiredPhrase = "qualification-profile-01-one-codex-planner-and-one-claude-critical-review";
    private const string RetiredContextBoundPhrase = "qualification-profile-01-one-context-bound-codex-planner-and-claude-reviewer";
    private const string RetiredWindowsRootPhrase = "qualification-profile-01-one-windows-root-codex-planner-and-claude-reviewer";
    private const string RetiredProposalGuidancePhrase =
        "qualification-profile-01-one-proposal-guidance-codex-planner-and-claude-reviewer";

    [Fact]
    public void The_successor_authorization_has_exactly_the_approved_flag_phrase_authorization_and_ledger_identity()
    {
        Assert.Equal("--run-qualification-profile-01-review-schema-exchange", QualificationOptIn.Flag);
        Assert.Equal(
            "qualification-profile-01-one-review-schema-codex-planner-and-claude-reviewer", QualificationOptIn.ConfirmationPhrase);
        Assert.Equal("owner-qualification-profile-01-lot-b-review-schema", InvocationLedger.Authorization);
        Assert.Equal("devalcopilot-qualification-profile-01-lot-b-review-schema-ledger", InvocationLedger.DirectoryName);
    }

    [Fact]
    public void The_retired_proposal_guidance_identity_is_still_named_exactly_so_that_it_can_be_refused()
    {
        Assert.Equal("--run-qualification-profile-01-proposal-guidance-exchange", QualificationOptIn.RetiredProposalGuidanceFlag);
        Assert.Equal(
            "devalcopilot-qualification-profile-01-lot-b-proposal-guidance-ledger",
            InvocationLedger.RetiredProposalGuidanceDirectoryName);
        Assert.NotEqual(QualificationOptIn.RetiredProposalGuidanceFlag, QualificationOptIn.Flag);
        Assert.NotEqual(RetiredProposalGuidancePhrase, QualificationOptIn.ConfirmationPhrase);
        Assert.NotEqual("owner-qualification-profile-01-lot-b-proposal-guidance", InvocationLedger.Authorization);
        Assert.NotEqual(InvocationLedger.RetiredProposalGuidanceDirectoryName, InvocationLedger.DirectoryName);
    }

    [Fact]
    public void The_retired_windows_root_identity_is_still_named_exactly_so_that_it_can_be_refused()
    {
        Assert.Equal("--run-qualification-profile-01-windows-root-exchange", QualificationOptIn.RetiredWindowsRootFlag);
        Assert.Equal(
            "devalcopilot-qualification-profile-01-lot-b-windows-root-ledger", InvocationLedger.RetiredWindowsRootDirectoryName);
        Assert.NotEqual(QualificationOptIn.RetiredWindowsRootFlag, QualificationOptIn.Flag);
        Assert.NotEqual(RetiredWindowsRootPhrase, QualificationOptIn.ConfirmationPhrase);
        Assert.NotEqual("owner-qualification-profile-01-lot-b-windows-root", InvocationLedger.Authorization);
        Assert.NotEqual(InvocationLedger.RetiredWindowsRootDirectoryName, InvocationLedger.DirectoryName);
    }

    [Fact]
    public void The_retired_context_bound_identity_is_still_named_exactly_so_that_it_can_be_refused()
    {
        Assert.Equal("--run-qualification-profile-01-context-bound-exchange", QualificationOptIn.RetiredContextBoundFlag);
        Assert.Equal(
            "devalcopilot-qualification-profile-01-lot-b-context-bound-ledger", InvocationLedger.RetiredContextBoundDirectoryName);
        Assert.NotEqual(QualificationOptIn.RetiredContextBoundFlag, QualificationOptIn.Flag);
        Assert.NotEqual(RetiredContextBoundPhrase, QualificationOptIn.ConfirmationPhrase);
        Assert.NotEqual("owner-qualification-profile-01-lot-b-context-bound", InvocationLedger.Authorization);
        Assert.NotEqual(InvocationLedger.RetiredContextBoundDirectoryName, InvocationLedger.DirectoryName);
        Assert.NotEqual(InvocationLedger.RetiredDirectoryName, InvocationLedger.DirectoryName);
    }

    [Theory]
    [InlineData(QualificationOptIn.RetiredFlag, QualificationOptIn.ConfirmationPhrase)]
    [InlineData(QualificationOptIn.RetiredFlag, RetiredPhrase)]
    [InlineData(QualificationOptIn.Flag, RetiredPhrase)]
    [InlineData(QualificationOptIn.RetiredContextBoundFlag, QualificationOptIn.ConfirmationPhrase)]
    [InlineData(QualificationOptIn.RetiredContextBoundFlag, RetiredContextBoundPhrase)]
    [InlineData(QualificationOptIn.Flag, RetiredContextBoundPhrase)]
    [InlineData(QualificationOptIn.RetiredWindowsRootFlag, QualificationOptIn.ConfirmationPhrase)]
    [InlineData(QualificationOptIn.RetiredWindowsRootFlag, RetiredWindowsRootPhrase)]
    [InlineData(QualificationOptIn.Flag, RetiredWindowsRootPhrase)]
    [InlineData(QualificationOptIn.RetiredProposalGuidanceFlag, QualificationOptIn.ConfirmationPhrase)]
    [InlineData(QualificationOptIn.RetiredProposalGuidanceFlag, RetiredProposalGuidancePhrase)]
    [InlineData(QualificationOptIn.Flag, RetiredProposalGuidancePhrase)]
    public void The_retired_lot_b_opt_ins_are_never_granted(string flag, string phrase)
    {
        string? Read(string name) => name == QualificationOptIn.ConfirmationVariable ? phrase : null;

        Assert.False(QualificationOptIn.IsGranted([flag], Read));
        Assert.False(QualificationOptIn.IsGranted([flag, ApprovedLaunchManifest.Option, @"C:\owner\approved.json"], Read));
        Assert.NotEqual(QualificationOptIn.RetiredFlag, QualificationOptIn.Flag);
        Assert.NotEqual(RetiredPhrase, QualificationOptIn.ConfirmationPhrase);
    }

    [Theory]
    [InlineData(QualificationOptIn.RetiredFlag, QualificationOptIn.ConfirmationPhrase)]
    [InlineData(QualificationOptIn.RetiredFlag, RetiredPhrase)]
    [InlineData(QualificationOptIn.RetiredFlag, null)]
    [InlineData(QualificationOptIn.RetiredContextBoundFlag, QualificationOptIn.ConfirmationPhrase)]
    [InlineData(QualificationOptIn.RetiredContextBoundFlag, RetiredContextBoundPhrase)]
    [InlineData(QualificationOptIn.RetiredContextBoundFlag, null)]
    [InlineData(QualificationOptIn.RetiredWindowsRootFlag, QualificationOptIn.ConfirmationPhrase)]
    [InlineData(QualificationOptIn.RetiredWindowsRootFlag, RetiredWindowsRootPhrase)]
    [InlineData(QualificationOptIn.RetiredWindowsRootFlag, null)]
    [InlineData(QualificationOptIn.RetiredProposalGuidanceFlag, QualificationOptIn.ConfirmationPhrase)]
    [InlineData(QualificationOptIn.RetiredProposalGuidanceFlag, RetiredProposalGuidancePhrase)]
    [InlineData(QualificationOptIn.RetiredProposalGuidanceFlag, null)]
    public async Task A_retired_flag_is_refused_with_a_fixed_message_before_any_environment_directory_or_ledger_entry(
        string retiredFlag, string? phrase)
    {
        var created = 0;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await LauncherEntry.RunAsync(
            [retiredFlag, ApprovedLaunchManifest.Option, @"C:\owner\approved.json"],
            name => name == QualificationOptIn.ConfirmationVariable ? phrase : null,
            Ledger,
            Tiny,
            () => { created++; return new ScriptedEnvironment(); },
            output,
            error,
            CancellationToken.None);

        Assert.Equal(LauncherEntry.RefusedExitCode, exitCode);
        Assert.Equal(0, created);
        Assert.False(Directory.Exists(LedgerDirectory));
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(
            $"Refused: {retiredFlag} is retired; its allowance and process slots were consumed. "
            + $"The replacement entry is {QualificationOptIn.Flag}.{Environment.NewLine}",
            error.ToString());
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
