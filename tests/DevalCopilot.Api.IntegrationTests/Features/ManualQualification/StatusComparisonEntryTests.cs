using System.Security.Cryptography;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using DevalCopilot.Application.Features.Processes.Ports;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>Lot A behind its entry point, over owned doubles: the exact opt-in, the binary identity gate, the spent-slot refusal, the
/// closed summary, the bounds and the cleanup rule. No real provider, binary, environment or account is involved.</summary>
public sealed class StatusComparisonEntryTests : QualificationTestBase
{
    private static readonly Dictionary<string, string> Ordinary = new() { ["USERPROFILE"] = "home", ["ANTHROPIC_API_KEY"] = "sk-secret" };
    private static readonly Dictionary<string, string> Filtered = new() { ["USERPROFILE"] = "home", ["SystemRoot"] = "root" };

    private readonly string _bin;
    private readonly string _claude;
    private readonly string _digest;
    private readonly string _ledger;

    public StatusComparisonEntryTests()
    {
        _bin = Directory.CreateDirectory(Path.Combine(LedgerDirectory, "bin")).FullName;
        _claude = Path.Combine(_bin, "claude.exe");
        File.WriteAllBytes(_claude, [1, 2, 3, 4, 5, 6, 7, 8]);
        _digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(_claude)));
        _ledger = Path.Combine(LedgerDirectory, "lot-a");
    }

    private static string? Confirmed(string name) =>
        name == QualificationOptIn.ConfirmationVariable ? StatusComparisonOptIn.ConfirmationPhrase : null;

    private string[] Arguments(string? path = null, string? digest = null) =>
        [StatusComparisonOptIn.Flag, StatusComparisonOptIn.ExecutableOption, path ?? _claude, StatusComparisonOptIn.Sha256Option, digest ?? _digest];

    private static readonly StatusLimits Quick = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));

    private async Task<(int Exit, string Output, string Error)> RunAsync(
        IReadOnlyList<string> arguments,
        Func<IProcessExecutionAdapter> adapter,
        Func<string, string?>? readVariable = null,
        Func<Dictionary<string, string>?>? filtered = null,
        StatusLimits? limits = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await StatusComparisonEntry.RunAsync(
            arguments, readVariable ?? Confirmed, Ordinary, filtered ?? (() => Filtered), _ledger, Path.GetTempPath(), limits ?? Quick, adapter, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }

    private static string[] StatusRoots() => Directory.GetDirectories(Path.GetTempPath(), OwnedRootGuard.RootPrefix + "status-*");

    [Fact]
    public async Task The_exact_opt_in_runs_both_observations_prints_only_the_closed_summary_keeps_a_copy_and_removes_only_its_own_root()
    {
        var before = StatusRoots();
        var adapter = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated(@"C:\Users\A\.claude"), ScriptedStatusAdapter.Authenticated(@"C:\Users\A\.claude"));

        var (exit, output, error) = await RunAsync(Arguments(), () => adapter);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error);
        Assert.Equal(2, adapter.Requests.Count);
        Assert.Equal(before, StatusRoots());
        using var summary = JsonDocument.Parse(output);
        var root = summary.RootElement;
        Assert.Equal(1, root.GetProperty("statusSchemaVersion").GetInt32());
        Assert.Equal(ClaudeStatusSummary.Authorization, root.GetProperty("authorization").GetString());
        Assert.Equal("BothAuthenticated", root.GetProperty("decision").GetString());
        Assert.Equal("Equal", root.GetProperty("comparison").GetProperty("configDirectory").GetString());
        Assert.True(root.GetProperty("binary").GetProperty("verified").GetBoolean());
        Assert.Equal(_digest, root.GetProperty("binary").GetProperty("sha256").GetString());
        Assert.True(root.GetProperty("cleanup").GetProperty("ownedRootRemoved").GetBoolean());
        Assert.False(root.GetProperty("rawOutputPersisted").GetBoolean());
        Assert.False(root.GetProperty("descendantShutdownClaimed").GetBoolean());
        Assert.Equal(2, root.GetProperty("processAdmission").GetProperty("totalAdmitted").GetInt32());
        Assert.True(root.GetProperty("environment").GetProperty("ordinary").GetProperty("hasAnthropicVariable").GetBoolean());
        var saved = Assert.Single(Directory.GetFiles(_ledger, "status-summary-*.json"));
        Assert.Equal(output.Trim(), File.ReadAllText(saved).Trim());
        Assert.Equal(2, Directory.GetFiles(_ledger, "admission-claude.status.*.start").Length);
    }

    [Fact]
    public async Task The_summary_carries_no_account_email_path_environment_name_or_value_or_raw_output()
    {
        var adapter = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated(@"C:\Users\Someone\.claude"), ScriptedStatusAdapter.LoggedOut());

        var (_, output, _) = await RunAsync(Arguments(), () => adapter);

        foreach (var forbidden in new[] { "someone", "example.invalid", "sk-secret", "ANTHROPIC_API_KEY", "SystemRoot", _claude, @".claude", "loggedIn" })
        {
            Assert.DoesNotContain(forbidden, output, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotMatch("[A-Za-z]:[/\\\\]", output);
    }

    [Theory]
    [InlineData(true, false, "OrdinaryAuthenticatedFilteredLoggedOut")]
    [InlineData(false, false, "BothLoggedOut")]
    public async Task A_completed_observation_that_is_not_both_authenticated_exits_five_with_its_decision(bool ordinary, bool filtered, string decision)
    {
        var adapter = new ScriptedStatusAdapter(
            ordinary ? ScriptedStatusAdapter.Authenticated() : ScriptedStatusAdapter.LoggedOut(),
            filtered ? ScriptedStatusAdapter.Authenticated() : ScriptedStatusAdapter.LoggedOut());

        var (exit, output, _) = await RunAsync(Arguments(), () => adapter);

        Assert.Equal(StatusComparisonEntry.NotAuthenticatedExitCode, exit);
        using var summary = JsonDocument.Parse(output);
        Assert.Equal(decision, summary.RootElement.GetProperty("decision").GetString());
        Assert.True(summary.RootElement.GetProperty("cleanup").GetProperty("ownedRootRemoved").GetBoolean());
    }

    public static TheoryData<string> BadArguments => new()
    {
        { "" },
        { "extra" },
        { "missing" },
        { "wrong-flag" },
        { "wrong-order" },
        { "relative-path" },
        { "short-digest" },
        { "non-hex-digest" },
    };

    [Theory]
    [MemberData(nameof(BadArguments))]
    public async Task Any_inexact_argument_list_is_refused_before_any_directory_ledger_or_process(string defect)
    {
        var created = 0;
        var good = Arguments();
        string[] arguments = defect switch
        {
            "" => [],
            "extra" => [.. good, "--again"],
            "missing" => good[..4],
            "wrong-flag" => ["--run-real-provider-session", .. good[1..]],
            "wrong-order" => [good[0], good[3], good[4], good[1], good[2]],
            "relative-path" => [good[0], good[1], "claude.exe", good[3], good[4]],
            "short-digest" => [good[0], good[1], good[2], good[3], good[4][..63]],
            _ => [good[0], good[1], good[2], good[3], new string('z', 64)],
        };

        var (exit, output, error) = await RunAsync(arguments, () => { created++; return new ScriptedStatusAdapter(); });

        Assert.Equal(StatusComparisonEntry.RefusedExitCode, exit);
        Assert.Equal(0, created);
        Assert.Equal(string.Empty, output);
        Assert.StartsWith("Refused:", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_ledger));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("one-codex-planner-and-one-claude-critical-review")]
    [InlineData(QualificationOptIn.ConfirmationPhrase)]
    public async Task A_missing_or_foreign_confirmation_is_refused_including_the_other_lots_phrase(string? phrase)
    {
        var created = 0;

        var (exit, _, _) = await RunAsync(
            Arguments(),
            () => { created++; return new ScriptedStatusAdapter(); },
            name => name == QualificationOptIn.ConfirmationVariable ? phrase : null);

        Assert.Equal(StatusComparisonEntry.RefusedExitCode, exit);
        Assert.Equal(0, created);
        Assert.False(Directory.Exists(_ledger));
    }

    [Fact]
    public void Lot_b_refuses_the_lot_a_flag_and_lot_a_refuses_the_lot_b_flag()
    {
        Assert.False(QualificationOptIn.IsGranted([StatusComparisonOptIn.Flag], name => StatusComparisonOptIn.ConfirmationPhrase));
        Assert.Null(StatusComparisonOptIn.TryParse([QualificationOptIn.Flag], Confirmed));
        Assert.NotEqual(QualificationOptIn.Flag, StatusComparisonOptIn.Flag);
        Assert.NotEqual(QualificationOptIn.ConfirmationPhrase, StatusComparisonOptIn.ConfirmationPhrase);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("missing")]
    [InlineData("name")]
    [InlineData("alias")]
    [InlineData("empty")]
    public async Task A_binary_that_is_not_exactly_the_approved_one_is_refused_before_any_slot_directory_or_process(string defect)
    {
        var path = _claude;
        var digest = _digest;
        switch (defect)
        {
            case "digest":
                digest = new string('a', 64);
                break;
            case "missing":
                path = Path.Combine(_bin, "gone", "claude.exe");
                break;
            case "name":
                path = Path.Combine(_bin, "other.exe");
                File.Copy(_claude, path);
                break;
            case "alias":
                path = Path.Combine(_bin, "alias", "claude.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                try
                {
                    File.CreateSymbolicLink(path, _claude);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return;
                }

                break;
            default:
                path = Path.Combine(_bin, "empty", "claude.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, []);
                break;
        }

        var created = 0;

        var (exit, output, error) = await RunAsync(Arguments(path, digest), () => { created++; return new ScriptedStatusAdapter(); });

        Assert.Equal(StatusComparisonEntry.RefusedExitCode, exit);
        Assert.Equal(0, created);
        Assert.Equal(string.Empty, output);
        Assert.StartsWith("Refused: Binary", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_ledger));
    }

    [Fact]
    public async Task A_second_entry_over_the_same_authorization_is_refused_before_any_process_and_nothing_is_rearmed()
    {
        var first = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated(), ScriptedStatusAdapter.Authenticated());
        await RunAsync(Arguments(), () => first);
        var created = 0;

        var (exit, output, error) = await RunAsync(Arguments(), () => { created++; return new ScriptedStatusAdapter(); });

        Assert.Equal(StatusComparisonEntry.SpentExitCode, exit);
        Assert.Equal(0, created);
        Assert.Equal(string.Empty, output);
        Assert.Contains("already spent", error, StringComparison.Ordinal);
        Assert.Equal(2, Directory.GetFiles(_ledger, "admission-claude.status.*.start").Length);
    }

    [Fact]
    public async Task A_single_consumed_slot_from_an_interrupted_entry_also_makes_the_authorization_spent()
    {
        var counters = new AdmissionLedger(_ledger, ClaudeStatusSummary.Authorization, TimeProvider.System);
        Assert.Null(counters.TryAdmit("claude.status.ordinary", 1, 2));
        var created = 0;

        var (exit, _, _) = await RunAsync(Arguments(), () => { created++; return new ScriptedStatusAdapter(); });

        Assert.Equal(StatusComparisonEntry.SpentExitCode, exit);
        Assert.Equal(0, created);
    }

    [Fact]
    public async Task When_the_production_builder_refuses_nothing_is_started_and_the_stop_is_reported()
    {
        var adapter = new ScriptedStatusAdapter();

        var (exit, output, _) = await RunAsync(Arguments(), () => adapter, filtered: () => null);

        Assert.Equal(StatusComparisonEntry.NotAuthenticatedExitCode, exit);
        Assert.Empty(adapter.Requests);
        using var summary = JsonDocument.Parse(output);
        Assert.Equal("Stopped", summary.RootElement.GetProperty("decision").GetString());
        Assert.Equal("FilteredEnvironmentUnavailable", summary.RootElement.GetProperty("stopCode").GetString());
    }

    [Fact]
    public async Task An_incoherent_observation_that_is_not_proven_finished_preserves_the_root()
    {
        var before = StatusRoots();
        var adapter = new ScriptedStatusAdapter(
            _ => Task.FromResult(ScriptedStatusAdapter.Result(ProcessExecutionOutcome.TimedOut, null, string.Empty)));

        var (exit, output, _) = await RunAsync(Arguments(), () => adapter);

        Assert.Equal(StatusComparisonEntry.NotAuthenticatedExitCode, exit);
        using var summary = JsonDocument.Parse(output);
        Assert.False(summary.RootElement.GetProperty("cleanup").GetProperty("ownedRootRemoved").GetBoolean());
        Assert.Equal("ObservationNotComplete", summary.RootElement.GetProperty("cleanup").GetProperty("reason").GetString());
        var created = StatusRoots().Except(before).Single();
        Directory.Delete(created, recursive: true);
    }

    [Fact]
    public async Task A_sequence_still_running_at_the_outer_bound_is_reported_unproven_and_touches_nothing()
    {
        var before = StatusRoots();
        var adapter = new ScriptedStatusAdapter(async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            return ScriptedStatusAdapter.Result(ProcessExecutionOutcome.Exited, 0, "{\"loggedIn\":true}");
        });
        var limits = new StatusLimits(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(300));

        var (exit, output, _) = await RunAsync(Arguments(), () => adapter, limits: limits);

        Assert.Equal(StatusComparisonEntry.OuterDeadlineExitCode, exit);
        using var summary = JsonDocument.Parse(output);
        Assert.Equal("OuterDeadlineExceeded", summary.RootElement.GetProperty("stopCode").GetString());
        Assert.False(summary.RootElement.GetProperty("cleanup").GetProperty("ownedRootRemoved").GetBoolean());
        Assert.Equal("Stopped", summary.RootElement.GetProperty("decision").GetString());
        Directory.Delete(StatusRoots().Except(before).Single(), recursive: true);
    }

    [Fact]
    public async Task An_unexpected_failure_reports_only_the_exception_type()
    {
        var (exit, output, error) = await RunAsync(Arguments(), () => throw new InvalidOperationException("C:/Users/someone/secret"));

        Assert.Equal(StatusComparisonEntry.UnexpectedFailureExitCode, exit);
        Assert.Equal(string.Empty, output);
        Assert.Equal("The entry stopped unexpectedly (InvalidOperationException)." + Environment.NewLine, error);
    }

    // QP-R1: one monotonic deadline for the whole entry ---------------------------------------------------------------------

    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(3);

    private static void RemoveNewRoots(string[] before)
    {
        foreach (var created in StatusRoots().Except(before))
        {
            Directory.Delete(created, recursive: true);
        }
    }

    [Fact]
    public async Task A_blocking_filtered_map_preparation_is_inside_the_bound_and_nothing_starts_after_it()
    {
        // The reviewer's control: only the filtered-map preparation blocks (400 ms) against a 40 ms outer budget.
        var before = StatusRoots();
        var adapter = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated(), ScriptedStatusAdapter.Authenticated());
        var limits = new StatusLimits(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(40));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var (exit, output, _) = await RunAsync(
            Arguments(), () => adapter, filtered: () => { Thread.Sleep(400); return Filtered; }, limits: limits);

        Assert.Equal(StatusComparisonEntry.OuterDeadlineExitCode, exit);
        Assert.True(clock.Elapsed < Promptly);
        using (var summary = JsonDocument.Parse(output))
        {
            Assert.Equal("OuterDeadlineExceeded", summary.RootElement.GetProperty("stopCode").GetString());
            Assert.Equal("NotStarted", summary.RootElement.GetProperty("observations")[0].GetProperty("physical").GetString());
        }

        await Task.Delay(TimeSpan.FromMilliseconds(900));
        Assert.Empty(adapter.Requests);
        Assert.Empty(Directory.Exists(_ledger) ? Directory.GetFiles(_ledger, "admission-claude.status.*.start") : []);
        Assert.Single(Directory.GetFiles(_ledger, "status-summary-*.json"));
        RemoveNewRoots(before);
    }

    [Fact]
    public async Task A_blocking_adapter_factory_is_inside_the_bound_too()
    {
        var before = StatusRoots();
        var adapter = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated(), ScriptedStatusAdapter.Authenticated());
        var limits = new StatusLimits(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(40));

        var (exit, _, _) = await RunAsync(Arguments(), () => { Thread.Sleep(400); return adapter; }, limits: limits);

        Assert.Equal(StatusComparisonEntry.OuterDeadlineExitCode, exit);
        await Task.Delay(TimeSpan.FromMilliseconds(900));
        Assert.Empty(adapter.Requests);
        RemoveNewRoots(before);
    }

    [Fact]
    public async Task An_admitted_observation_is_never_reported_not_started_because_the_bound_elapsed()
    {
        var before = StatusRoots();
        var adapter = new ScriptedStatusAdapter(async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            return ScriptedStatusAdapter.Result(ProcessExecutionOutcome.Exited, 0, "{\"loggedIn\":true}");
        });
        var limits = new StatusLimits(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(400));

        var (exit, output, _) = await RunAsync(Arguments(), () => adapter, limits: limits);

        Assert.Equal(StatusComparisonEntry.OuterDeadlineExitCode, exit);
        using var summary = JsonDocument.Parse(output);
        var observations = summary.RootElement.GetProperty("observations");
        Assert.Equal("StartedUnproven", observations[0].GetProperty("physical").GetString());
        Assert.Equal("NotStarted", observations[1].GetProperty("physical").GetString());
        Assert.Equal(1, summary.RootElement.GetProperty("processAdmission").GetProperty("admitted").GetProperty("claude.status.ordinary").GetInt32());
        Assert.Equal("OuterDeadlineExceeded", summary.RootElement.GetProperty("processAdmission").GetProperty("closedBy").GetString());
        Assert.False(summary.RootElement.GetProperty("cleanup").GetProperty("ownedRootRemoved").GetBoolean());
        RemoveNewRoots(before);
    }

    [Fact]
    public async Task A_held_report_sink_cannot_hold_the_entry_and_the_late_release_saves_nothing()
    {
        var before = StatusRoots();
        var adapter = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated(), ScriptedStatusAdapter.Authenticated());
        var limits = new StatusLimits(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(600));
        using var released = new ManualResetEventSlim();
        using var output = new HeldWriter(released);
        using var error = new StringWriter();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var exit = await StatusComparisonEntry.RunAsync(
            Arguments(), Confirmed, Ordinary, () => Filtered, _ledger, Path.GetTempPath(), limits, () => adapter, output, error, CancellationToken.None);

        Assert.Equal(StatusComparisonEntry.OuterDeadlineExitCode, exit);
        Assert.True(clock.Elapsed < Promptly);
        Assert.Contains("unproven", error.ToString(), StringComparison.Ordinal);
        released.Set();
        await Task.Delay(300);
        // Only the unproven verdict is persisted; the late release saves nothing more.
        var saved = Assert.Single(Directory.GetFiles(_ledger, "status-summary-*.json"));
        using var verdict = JsonDocument.Parse(File.ReadAllText(saved));
        Assert.Equal("OuterDeadlineExceeded", verdict.RootElement.GetProperty("stopCode").GetString());
        RemoveNewRoots(before);
    }

    [Fact]
    public async Task A_replaced_binary_is_refused_before_the_second_start_with_a_closed_drift_code()
    {
        var before = StatusRoots();
        var adapter = new ScriptedStatusAdapter(
            _ =>
            {
                File.AppendAllText(_claude, "replaced after the first start");
                return Task.FromResult(ScriptedStatusAdapter.Result(ProcessExecutionOutcome.Exited, 0, "{\"loggedIn\":true,\"authMethod\":\"claude.ai\"}"));
            },
            ScriptedStatusAdapter.Authenticated());

        var (exit, output, _) = await RunAsync(Arguments(), () => adapter);

        Assert.Equal(StatusComparisonEntry.NotAuthenticatedExitCode, exit);
        Assert.Single(adapter.Requests);
        using var summary = JsonDocument.Parse(output);
        Assert.Equal("FilteredObservationNotCoherent", summary.RootElement.GetProperty("stopCode").GetString());
        Assert.Equal("Refused", summary.RootElement.GetProperty("observations")[1].GetProperty("physical").GetString());
        var admission = summary.RootElement.GetProperty("processAdmission");
        Assert.Equal(1, admission.GetProperty("refused").GetProperty("FileIdentityDrift").GetInt32());
        Assert.Equal("FileIdentityDrift", admission.GetProperty("closedBy").GetString());
        Assert.False(admission.GetProperty("admitted").TryGetProperty("claude.status.filtered", out _));
        RemoveNewRoots(before);
    }

    [Theory]
    [InlineData("authMethod")]
    [InlineData("configDirectory")]
    public async Task A_mistyped_first_report_stops_the_entry_before_the_second_start(string member)
    {
        var before = StatusRoots();
        var adapter = new ScriptedStatusAdapter(
            ScriptedStatusAdapter.Exited(0, $"{{\"loggedIn\":true,\"{member}\":123}}"),
            ScriptedStatusAdapter.Authenticated());

        var (exit, output, _) = await RunAsync(Arguments(), () => adapter);

        Assert.Equal(StatusComparisonEntry.NotAuthenticatedExitCode, exit);
        Assert.Single(adapter.Requests);
        using var summary = JsonDocument.Parse(output);
        Assert.Equal("OrdinaryObservationNotCoherent", summary.RootElement.GetProperty("stopCode").GetString());
        Assert.Equal("Unknown", summary.RootElement.GetProperty("observations")[0].GetProperty("reported").GetString());
        Assert.Equal("NotStarted", summary.RootElement.GetProperty("observations")[1].GetProperty("physical").GetString());
        RemoveNewRoots(before);
    }

    private sealed class HeldWriter(ManualResetEventSlim released) : StringWriter
    {
        public override Task WriteLineAsync(string? value) => Task.Run(() => released.Wait());
    }
}
