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
    // The original lot A entry's phrase and authorization, built here only to prove that they are refused or distinct.
    private const string RetiredPhrase = "qualification-profile-01-two-claude-auth-status-observations";
    private const string RetiredAuthorization = "owner-qualification-profile-01-lot-a-two-claude-auth-status-observations";

    private static readonly Dictionary<string, string> Ordinary = new() { ["USERPROFILE"] = "home", ["ANTHROPIC_API_KEY"] = "sk-secret" };
    private static readonly Dictionary<string, string> Filtered = new() { ["USERPROFILE"] = "home", ["SystemRoot"] = "root" };

    private readonly List<string> _junctions = [];
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
        StatusLimits? limits = null,
        string? ledger = null,
        Func<string, bool>? inspectLedger = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await StatusComparisonEntry.RunAsync(
            arguments, readVariable ?? Confirmed, Ordinary, filtered ?? (() => Filtered), ledger ?? _ledger, Path.GetTempPath(), limits ?? Quick, adapter, output, error, CancellationToken.None, inspectLedger);
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
    [InlineData(RetiredPhrase)]
    public async Task A_missing_or_foreign_confirmation_is_refused_including_the_other_lots_and_the_retired_phrase(string? phrase)
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

    [Fact]
    public void The_post_login_entry_has_its_own_flag_phrase_authorization_and_ledger_distinct_from_the_retired_original()
    {
        Assert.Equal("--run-claude-auth-status-after-login", StatusComparisonOptIn.Flag);
        Assert.Equal("qualification-profile-01-two-claude-auth-status-observations-after-login", StatusComparisonOptIn.ConfirmationPhrase);
        Assert.Equal("owner-qualification-profile-01-lot-a-post-login", ClaudeStatusSummary.Authorization);
        Assert.Equal("devalcopilot-qualification-profile-01-lot-a-post-login-ledger", StatusComparisonEntry.LedgerDirectoryName);
        Assert.NotEqual(StatusComparisonOptIn.RetiredFlag, StatusComparisonOptIn.Flag);
        Assert.NotEqual(RetiredPhrase, StatusComparisonOptIn.ConfirmationPhrase);
        Assert.NotEqual(RetiredAuthorization, ClaudeStatusSummary.Authorization);
        Assert.NotEqual(StatusComparisonEntry.RetiredLedgerDirectoryName, StatusComparisonEntry.LedgerDirectoryName);
        Assert.Equal("--run-claude-auth-status-comparison", StatusComparisonOptIn.RetiredFlag);
        Assert.Equal("devalcopilot-qualification-profile-01-lot-a-ledger", StatusComparisonEntry.RetiredLedgerDirectoryName);
    }

    [Theory]
    [InlineData(RetiredPhrase)]
    [InlineData(StatusComparisonOptIn.ConfirmationPhrase)]
    public async Task The_retired_original_flag_is_refused_with_either_phrase_before_any_directory_ledger_or_process(string phrase)
    {
        var created = 0;
        string[] arguments = [StatusComparisonOptIn.RetiredFlag, StatusComparisonOptIn.ExecutableOption, _claude, StatusComparisonOptIn.Sha256Option, _digest];

        var (exit, output, error) = await RunAsync(
            arguments,
            () => { created++; return new ScriptedStatusAdapter(); },
            name => name == QualificationOptIn.ConfirmationVariable ? phrase : null);

        Assert.Equal(StatusComparisonEntry.RefusedExitCode, exit);
        Assert.Equal(0, created);
        Assert.Equal(string.Empty, output);
        Assert.Contains("retired", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_ledger));
        Assert.Null(StatusComparisonOptIn.TryParse(arguments, name => phrase));
    }

    [Fact]
    public async Task The_new_flag_with_the_retired_phrase_is_refused_before_any_directory_ledger_or_process()
    {
        var created = 0;

        var (exit, _, _) = await RunAsync(
            Arguments(),
            () => { created++; return new ScriptedStatusAdapter(); },
            name => name == QualificationOptIn.ConfirmationVariable ? RetiredPhrase : null);

        Assert.Equal(StatusComparisonEntry.RefusedExitCode, exit);
        Assert.Equal(0, created);
        Assert.False(Directory.Exists(_ledger));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_ledger_pointed_at_the_retired_original_directory_is_refused_with_no_file_access(bool trailingSeparator)
    {
        var retired = Path.Combine(LedgerDirectory, StatusComparisonEntry.RetiredLedgerDirectoryName);
        var created = 0;

        var (exit, output, error) = await RunAsync(
            Arguments(),
            () => { created++; return new ScriptedStatusAdapter(); },
            ledger: retired + (trailingSeparator ? Path.DirectorySeparatorChar : string.Empty));

        Assert.Equal(StatusComparisonEntry.RefusedExitCode, exit);
        Assert.Equal(0, created);
        Assert.Equal(string.Empty, output);
        Assert.Contains("retired ledger", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(retired));
    }

    [Fact]
    public async Task The_post_login_entry_uses_only_its_own_ledger_and_leaves_a_spent_retired_ledger_untouched()
    {
        var retired = Directory.CreateDirectory(Path.Combine(LedgerDirectory, StatusComparisonEntry.RetiredLedgerDirectoryName)).FullName;
        File.WriteAllText(Path.Combine(retired, "admission-claude.status.ordinary-1.start"), $"{{\"authorization\":\"{RetiredAuthorization}\"}}");
        File.WriteAllText(Path.Combine(retired, "admission-claude.status.filtered-1.start"), $"{{\"authorization\":\"{RetiredAuthorization}\"}}");
        File.WriteAllText(Path.Combine(retired, "status-summary-original.json"), "{\"decision\":\"BothLoggedOut\"}");
        var snapshot = Snapshot(retired);
        var adapter = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated(), ScriptedStatusAdapter.Authenticated());

        var (exit, output, _) = await RunAsync(Arguments(), () => adapter);

        Assert.Equal(0, exit);
        Assert.Equal(2, adapter.Requests.Count);
        Assert.Equal(snapshot, Snapshot(retired));
        using var summary = JsonDocument.Parse(output);
        Assert.Equal(ClaudeStatusSummary.Authorization, summary.RootElement.GetProperty("authorization").GetString());
        Assert.Equal(2, Directory.GetFiles(_ledger, "admission-claude.status.*.start").Length);
        Assert.Single(Directory.GetFiles(_ledger, "status-summary-*.json"));
        Assert.Equal(
            new[] { "bin", StatusComparisonEntry.RetiredLedgerDirectoryName, "lot-a" },
            Directory.GetDirectories(LedgerDirectory).Select(directory => Path.GetFileName(directory)).Order(StringComparer.Ordinal));
    }

    // PAL-R1: a junction or other alias at the ledger destination is refused without being followed -------------------------------

    private string FabricatedRetiredLedger()
    {
        var retired = Directory.CreateDirectory(Path.Combine(LedgerDirectory, "fabricated-original", StatusComparisonEntry.RetiredLedgerDirectoryName)).FullName;
        File.WriteAllText(Path.Combine(retired, "admission-claude.status.ordinary-1.start"), $"{{\"authorization\":\"{RetiredAuthorization}\"}}");
        File.WriteAllText(Path.Combine(retired, "admission-claude.status.filtered-1.start"), $"{{\"authorization\":\"{RetiredAuthorization}\"}}");
        File.WriteAllText(Path.Combine(retired, "status-summary-original.json"), "{\"decision\":\"BothLoggedOut\"}");
        return retired;
    }

    public override void Dispose()
    {
        // A recursive delete of the fixture root refuses a junction inside it: remove each junction itself (never its target) first.
        foreach (var link in _junctions.Where(Directory.Exists))
        {
            Directory.Delete(link, recursive: false);
        }

        base.Dispose();
    }

    private void CreateJunction(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        _junctions.Add(link);
        Assert.Equal(0, process.ExitCode);
        Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint));
    }

    [Fact]
    public async Task A_junction_to_a_fabricated_retired_ledger_is_refused_as_an_alias_not_reported_spent_and_stays_untouched()
    {
        var retired = FabricatedRetiredLedger();
        var link = Path.Combine(LedgerDirectory, "alias", "post-login-ledger");
        CreateJunction(link, retired);
        var snapshot = Snapshot(retired);
        var created = 0;

        var (exit, output, error) = await RunAsync(Arguments(), () => { created++; return new ScriptedStatusAdapter(); }, ledger: link);

        Assert.Equal(StatusComparisonEntry.RefusedExitCode, exit);
        Assert.Equal(0, created);
        Assert.Equal(string.Empty, output);
        Assert.Contains("junction or other alias", error, StringComparison.Ordinal);
        Assert.DoesNotContain("spent", error, StringComparison.Ordinal);
        Assert.Equal(snapshot, Snapshot(retired));
    }

    [Fact]
    public async Task A_junction_to_any_other_directory_is_refused_and_nothing_is_created_in_the_target()
    {
        var target = Directory.CreateDirectory(Path.Combine(LedgerDirectory, "elsewhere")).FullName;
        var link = Path.Combine(LedgerDirectory, "alias", "post-login-ledger");
        CreateJunction(link, target);
        var created = 0;

        var (exit, output, _) = await RunAsync(Arguments(), () => { created++; return new ScriptedStatusAdapter(); }, ledger: link);

        Assert.Equal(StatusComparisonEntry.RefusedExitCode, exit);
        Assert.Equal(0, created);
        Assert.Equal(string.Empty, output);
        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    [Fact]
    public async Task A_destination_that_becomes_a_junction_during_the_run_gets_no_slot_and_no_normal_report_file()
    {
        var retired = FabricatedRetiredLedger();
        var snapshot = Snapshot(retired);
        var adapter = new ScriptedStatusAdapter(
            _ =>
            {
                Directory.Delete(_ledger, recursive: true);
                CreateJunction(_ledger, retired);
                return Task.FromResult(ScriptedStatusAdapter.Result(ProcessExecutionOutcome.Exited, 0, "{\"loggedIn\":true,\"authMethod\":\"claude.ai\"}"));
            },
            ScriptedStatusAdapter.Authenticated());
        var before = StatusRoots();

        var (exit, output, _) = await RunAsync(Arguments(), () => adapter);

        // The second slot was refused by the aliased destination, so the observations are not both authenticated (exit 5, never 0),
        // the printed summary stands, and nothing was written into the aliased target.
        Assert.Equal(StatusComparisonEntry.NotAuthenticatedExitCode, exit);
        Assert.Single(adapter.Requests);
        Assert.False(string.IsNullOrWhiteSpace(output));
        Assert.Equal(snapshot, Snapshot(retired));
        RemoveNewRoots(before);
    }

    [Fact]
    public async Task A_destination_that_becomes_a_junction_before_the_expiry_report_gets_no_expiry_summary_file()
    {
        var retired = FabricatedRetiredLedger();
        var snapshot = Snapshot(retired);
        var adapter = new ScriptedStatusAdapter(async _ =>
        {
            Directory.Delete(_ledger, recursive: true);
            CreateJunction(_ledger, retired);
            await Task.Delay(TimeSpan.FromSeconds(2));
            return ScriptedStatusAdapter.Result(ProcessExecutionOutcome.Exited, 0, "{\"loggedIn\":true}");
        });
        var limits = new StatusLimits(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(800));
        var before = StatusRoots();

        var (exit, output, _) = await RunAsync(Arguments(), () => adapter, limits: limits);

        Assert.Equal(StatusComparisonEntry.OuterDeadlineExitCode, exit);
        using var summary = JsonDocument.Parse(output);
        Assert.Equal("OuterDeadlineExceeded", summary.RootElement.GetProperty("stopCode").GetString());
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        Assert.Equal(snapshot, Snapshot(retired));
        RemoveNewRoots(before);
    }

    // PAL-R2: the destination metadata check runs inside the bounded worker --------------------------------------------------------

    /// <summary>An owned double of the metadata check: the call numbered <paramref name="holdCall"/> signals that it was entered and then
    /// blocks until released; every call answers "not an alias", the answer that would let a late continuation proceed.</summary>
    private sealed class HeldInspector(int holdCall) : IDisposable
    {
        private int _calls;

        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Released { get; } = new();

        public bool Inspect(string path)
        {
            if (Interlocked.Increment(ref _calls) == holdCall)
            {
                Entered.Set();
                Released.Wait(TimeSpan.FromSeconds(30));
            }

            return false;
        }

        public void Dispose()
        {
            Released.Set();
            Entered.Dispose();
            Released.Dispose();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task A_held_destination_check_cannot_hold_the_entry_and_its_late_release_starts_nothing_and_consumes_no_slot(int holdCall)
    {
        var before = StatusRoots();
        var adapter = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated(), ScriptedStatusAdapter.Authenticated());
        var created = 0;
        var limits = new StatusLimits(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(400));
        using var held = new HeldInspector(holdCall);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var (exit, output, _) = await RunAsync(
            Arguments(), () => { created++; return adapter; }, limits: limits, inspectLedger: held.Inspect);

        Assert.Equal(StatusComparisonEntry.OuterDeadlineExitCode, exit);
        Assert.True(clock.Elapsed < Promptly);
        Assert.True(held.Entered.IsSet);
        Assert.Equal(0, created);
        using (var summary = JsonDocument.Parse(output))
        {
            Assert.Equal("OuterDeadlineExceeded", summary.RootElement.GetProperty("stopCode").GetString());
            Assert.Equal("NotStarted", summary.RootElement.GetProperty("observations")[0].GetProperty("physical").GetString());
            Assert.Equal("NotStarted", summary.RootElement.GetProperty("observations")[1].GetProperty("physical").GetString());
        }

        held.Released.Set();
        await Task.Delay(TimeSpan.FromMilliseconds(700));

        // The late continuation found the deadline expired: no adapter, process, slot or root, and only the one unproven verdict file.
        Assert.Equal(0, created);
        Assert.Empty(adapter.Requests);
        Assert.Empty(Directory.Exists(_ledger) ? Directory.GetFiles(_ledger, "admission-*.start") : []);
        Assert.Equal(before, StatusRoots());
        Assert.True((Directory.Exists(_ledger) ? Directory.GetFiles(_ledger, "status-summary-*.json") : []).Length <= 1);
    }

    [Fact]
    public async Task A_held_destination_check_does_not_change_a_normal_run_once_it_is_not_held()
    {
        var adapter = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated(), ScriptedStatusAdapter.Authenticated());
        using var held = new HeldInspector(holdCall: int.MaxValue);

        var (exit, output, _) = await RunAsync(Arguments(), () => adapter, inspectLedger: held.Inspect);

        Assert.Equal(0, exit);
        Assert.Equal(2, adapter.Requests.Count);
        Assert.Equal(output.Trim(), File.ReadAllText(Assert.Single(Directory.GetFiles(_ledger, "status-summary-*.json"))).Trim());
    }

    [Fact]
    public async Task A_plain_valid_destination_still_runs_and_persists_exactly_as_before()
    {
        var adapter = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated(), ScriptedStatusAdapter.Authenticated());

        var (exit, output, _) = await RunAsync(Arguments(), () => adapter);

        Assert.Equal(0, exit);
        Assert.Equal(output.Trim(), File.ReadAllText(Assert.Single(Directory.GetFiles(_ledger, "status-summary-*.json"))).Trim());
        Assert.False(File.GetAttributes(_ledger).HasFlag(FileAttributes.ReparsePoint));
    }

    private static string[] Snapshot(string directory) =>
        [.. Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(entry => File.Exists(entry)
                ? $"{Path.GetRelativePath(directory, entry)}|{File.GetLastWriteTimeUtc(entry):O}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry)))}"
                : $"{Path.GetRelativePath(directory, entry)}|directory")];

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
