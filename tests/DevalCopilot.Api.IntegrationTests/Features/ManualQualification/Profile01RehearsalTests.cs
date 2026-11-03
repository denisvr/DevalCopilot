using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;
using DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>
/// The complete lot B composition (production host, authenticated routes, the new evidence facts, the admission boundary around every
/// host-issued and launcher-issued process and the durable counters) rehearsed against the journey's owned provider doubles only. It
/// resolves the exact process counts of the unchanged production flow, shows the closed inventory admits all of it, and shows an extra,
/// unlisted or exhausted process stopping the session. It is not a provider result.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class Profile01RehearsalTests : QualificationTestBase
{
    private const int LauncherId = 100;
    private static readonly DateTime Epoch = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ProcessEntry Explorer = new(10, 1, "explorer.exe", Epoch);
    private static readonly ProcessEntry Launcher = new(LauncherId, 10, "ManualQualification.exe", Epoch.AddMinutes(1));

    private static readonly QualificationLimits RehearsalLimits = new(
        Readiness: TimeSpan.FromSeconds(60),
        Setup: TimeSpan.FromSeconds(60),
        Submission: TimeSpan.FromSeconds(30),
        Read: TimeSpan.FromSeconds(20),
        PollInterval: TimeSpan.FromMilliseconds(200),
        Stage: TimeSpan.FromSeconds(120),
        Shutdown: TimeSpan.FromSeconds(60),
        Overall: TimeSpan.FromMinutes(8));

    /// <summary>The exact per-class counts the unchanged production flow issues for one Proposal and one review, resolved by this
    /// rehearsal. The three evidence classes and the launcher snapshots are deterministic; discovery is checked against its ceiling.</summary>
    private static readonly (string Name, int Count)[] ResolvedCounts =
    [
        ("role.codex.exec", 1), ("role.claude.print", 1),
        ("launcher.git.init", 1), ("launcher.git.config", 1), ("launcher.git.add", 1), ("launcher.git.commit", 1),
        ("launcher.git.head", 10), ("launcher.git.headref", 9), ("launcher.git.refs", 9), ("launcher.git.index", 9),
        ("git.revparse.gitdir", 3), ("git.revparse.bare", 2), ("git.revparse.toplevel", 2), ("git.revparse.commondir", 4),
        ("git.symbolicref", 4), ("git.revparse.head", 4), ("git.status.porcelain", 2), ("git.worktree.add", 1),
        ("git.evidence.revparse", 14), ("git.evidence.status", 14), ("git.evidence.diff", 14), ("git.evidence.hashobject", 8),
        ("git.evidence.lsfiles.tracked", 4), ("git.evidence.lsfiles.others", 4),
    ];

    [Fact]
    public async Task The_complete_flow_is_admitted_by_the_closed_inventory_with_the_resolved_counts_and_no_refusal()
    {
        using var rehearsal = await RehearseAsync();

        Assert.True(rehearsal.Report.Qualified, rehearsal.Summary);
        var facts = rehearsal.Report.Admission;
        Assert.Equal(0, facts.TotalRefused);
        foreach (var (name, count) in ResolvedCounts)
        {
            Assert.True(facts.Admitted.TryGetValue(name, out var admitted), $"{name} was never admitted. {rehearsal.Summary}");
            Assert.True(count == admitted, $"{name}: expected {count} admitted {admitted}. {string.Join(", ", facts.Admitted.Select(pair => pair.Key + "=" + pair.Value))}");
        }

        var discovery = facts.Admitted.Where(pair => pair.Key.StartsWith("discovery.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(7, discovery.Length);
        // The launcher's own bounded version observation of the approved Git (before its first fixture mutation) is one more
        // discovery.git start beside the host's own probe of it: at least two, still inside the eight-start ceiling.
        Assert.InRange(facts.Admitted["discovery.git"], 2, Profile01Inventory.DiscoveryPerCapability);
        Assert.All(discovery, pair => Assert.InRange(pair.Value, 1, Profile01Inventory.DiscoveryPerCapability));
        Assert.Equal(80, facts.Admitted.Where(pair => pair.Key.StartsWith("git.", StringComparison.Ordinal)).Sum(pair => pair.Value));
        Assert.Equal(41, facts.Admitted.Where(pair => pair.Key.StartsWith("launcher.git.", StringComparison.Ordinal)).Sum(pair => pair.Value));
        Assert.Equal(facts.TotalAdmitted, rehearsal.Counters.Consumed()[AdmissionLedger.TotalName]);
        Assert.Equal(ResolvedCounts.Length + 7, facts.Admitted.Count);
        Assert.False(facts.Admitted.ContainsKey("codex.appserver"));
    }

    [Fact]
    public async Task The_real_reader_carries_the_recorded_contract_io_and_evidence_route_facts_of_both_accepted_attempts()
    {
        using var rehearsal = await RehearseAsync();

        Assert.True(rehearsal.Report.Qualified, rehearsal.Summary);
        Assert.Equal(QualificationContracts.PlannerAdapter, rehearsal.Report.Planner!.AdapterContractVersion);
        Assert.Equal(QualificationContracts.ReviewerAdapter, rehearsal.Report.Reviewer!.AdapterContractVersion);
        Assert.All(new[] { rehearsal.Report.Planner, rehearsal.Report.Reviewer }, reading =>
        {
            Assert.True(reading.RequiredIoComplete);
            Assert.True(reading.AttemptEvidenceRouteAgrees);
            Assert.Equal("NotRecorded", reading.InvocationDiagnosticState);
            Assert.Null(reading.InvocationDiagnosticCategory);
        });
        using var summary = JsonDocument.Parse(rehearsal.Summary);
        Assert.Equal(3, summary.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(InvocationLedger.Authorization, summary.RootElement.GetProperty("authorization").GetString());
        Assert.True(summary.RootElement.GetProperty("reviewer").GetProperty("ioCompletion").GetProperty("requiredComplete").GetBoolean());
        Assert.Equal(0, summary.RootElement.GetProperty("processAdmission").GetProperty("totalRefused").GetInt32());
    }

    [Fact]
    public async Task A_failed_claude_attempt_with_no_message_is_read_through_the_real_routes_and_reported_with_its_closed_diagnostic()
    {
        using var rehearsal = await RehearseAsync(
            objective: FixtureRepository.Objective + " " + ManifestInfo.ReportedAuthenticationErrorMarker);

        Assert.Equal(QualificationResult.Failed, rehearsal.Report.Result);
        Assert.StartsWith("ReviewerAttemptFailed", rehearsal.Report.Code, StringComparison.Ordinal);
        var reviewer = rehearsal.Report.Reviewer!;
        Assert.Equal(QualificationContracts.ReviewerAdapter, reviewer.AdapterContractVersion);
        Assert.Equal(0, reviewer.MessageCount);
        Assert.Null(reviewer.MessageId);
        Assert.Equal("Exited", reviewer.ProcessOutcome);
        Assert.Equal(1, reviewer.ExitCode);
        Assert.True(reviewer.RequiredIoComplete);
        Assert.True(reviewer.AttemptEvidenceRouteAgrees);
        Assert.Equal("Recorded", reviewer.InvocationDiagnosticState);
        Assert.Equal("AuthenticationRequired", reviewer.InvocationDiagnosticCategory);
        Assert.Equal("agent-invocation-diagnostic-v1", reviewer.InvocationDiagnosticVersion);
        Assert.Equal(0, rehearsal.Report.Admission.TotalRefused);
        Assert.Equal(1, rehearsal.Report.Admission.Admitted["role.claude.print"]);
        Assert.DoesNotContain(ClaudeRole.ReportedErrorNarrative, rehearsal.Summary, StringComparison.Ordinal);
        using var summary = JsonDocument.Parse(rehearsal.Summary);
        var diagnostic = summary.RootElement.GetProperty("reviewer").GetProperty("invocationDiagnostic");
        Assert.Equal("AuthenticationRequired", diagnostic.GetProperty("category").GetString());
        Assert.Equal("Failed", summary.RootElement.GetProperty("result").GetString());
    }

    [Fact]
    public async Task A_lowered_ceiling_stops_the_real_flow_at_the_first_extra_process_without_a_second_role_or_a_refund()
    {
        using var rehearsal = await RehearseAsync(ceilings: new Dictionary<string, int> { ["git.evidence.diff"] = 2 });

        Assert.NotEqual(QualificationResult.Qualified, rehearsal.Report.Result);
        Assert.Equal("ProcessAdmissionRefused", rehearsal.Report.Code);
        Assert.True(rehearsal.Report.Admission.Refused.ContainsKey("ClassLimit.git.evidence.diff"), rehearsal.Summary);
        Assert.Equal(2, rehearsal.Report.Admission.Admitted["git.evidence.diff"]);
        Assert.False(rehearsal.Report.Admission.Admitted.ContainsKey("role.claude.print"));
        Assert.DoesNotContain("role.claude.print", rehearsal.Counters.Consumed().Keys);
    }

    [Fact]
    public async Task An_unlisted_discovery_probe_in_the_real_flow_is_refused_before_its_process_and_blocks_before_any_allowance()
    {
        using var rehearsal = await RehearseAsync(withoutClasses: ["discovery.docker"]);

        Assert.Equal(QualificationResult.Blocked, rehearsal.Report.Result);
        Assert.Equal("ProcessAdmissionRefused", rehearsal.Report.Code);
        Assert.Equal(1, rehearsal.Report.Admission.Refused["Unlisted"]);
        Assert.False(rehearsal.Report.PlannerConsumed);
        Assert.False(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(rehearsal.Report.Admission.Admitted.ContainsKey("role.codex.exec"));
    }

    [Fact]
    public async Task Without_the_judged_pins_no_host_git_or_role_shape_is_listed_and_the_session_is_blocked_with_nothing_spent()
    {
        using var rehearsal = await RehearseAsync(neverPin: true);

        Assert.NotEqual(QualificationResult.Qualified, rehearsal.Report.Result);
        Assert.Equal("ProcessAdmissionRefused", rehearsal.Report.Code);
        Assert.False(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(rehearsal.Report.Admission.Admitted.ContainsKey("role.codex.exec"));
    }

    [Fact]
    public async Task A_duplicate_entry_over_the_spent_counters_starts_nothing_at_all()
    {
        using var first = await RehearseAsync();
        Assert.True(first.Report.Qualified, first.Summary);
        var rootsBefore = Directory.GetDirectories(Path.GetTempPath(), OwnedRootGuard.RootPrefix + "manual-*");

        using var second = await RehearseAsync();

        Assert.Equal(QualificationResult.SessionSpent, second.Report.Result);
        Assert.Equal(0, second.Report.Admission.TotalAdmitted);
        Assert.Equal(rootsBefore, Directory.GetDirectories(Path.GetTempPath(), OwnedRootGuard.RootPrefix + "manual-*"));
    }

    private sealed class DoubleGitVersion(GitVersionObservation observation) : IGitVersionObserver
    {
        public int Calls { get; private set; }

        public IReadOnlyList<string> EnvironmentNames => [];

        public GitVersionObservation Observe(string gitPath, string workingDirectory, TimeSpan bound)
        {
            Calls++;
            return observation;
        }
    }

    [Theory]
    [InlineData("git version 2.31.1", "GitVersionTooOld")]
    [InlineData("git version", "GitVersionUnknown")]
    [InlineData("git version 2.50.0.windows.1\n", null)]
    public async Task The_real_flow_observes_the_approved_git_once_before_any_fixture_mutation_and_refuses_an_unsupported_one(string output, string? code)
    {
        var observer = new DoubleGitVersion(new GitVersionObservation(true, 0, false, false, output));

        using var rehearsal = await RehearseAsync(gitVersions: observer);

        Assert.Equal(1, observer.Calls);
        if (code is null)
        {
            Assert.True(rehearsal.Report.Qualified, rehearsal.Summary);
            return;
        }

        Assert.Equal(QualificationResult.Blocked, rehearsal.Report.Result);
        Assert.Equal(code, rehearsal.Report.Code);
        Assert.False(Ledger.IsConsumed(AllowanceRole.Planner));
        // No fixture Git command ran: the observation is the launcher's only Git start, and it is the discovery.git class.
        Assert.Equal(1, rehearsal.Report.Admission.Admitted["discovery.git"]);
        Assert.DoesNotContain(rehearsal.Report.Admission.Admitted.Keys, name => name.StartsWith("launcher.git.", StringComparison.Ordinal));
        Assert.Equal(0, rehearsal.Report.Admission.TotalRefused);
    }

    [Fact]
    public async Task The_three_codex_app_server_observers_are_refused_and_counted_never_launched_and_never_an_invented_readiness()
    {
        var (admission, _) = Profile01Composition.CreateAdmission(Path.Combine(LedgerDirectory, "observers"));
        var observers = new AdmissionRefusingCodexObservers(admission);

        var allowance = await ((ICodexAccountAllowanceAdapter)observers).ObserveAsync(@"C:\x\codex.exe", null, CancellationToken.None);
        var catalog = await ((ICodexModelCatalogAdapter)observers).ObserveAsync(@"C:\x\codex.exe", @"C:\x\codex.js", CancellationToken.None);
        var usage = await ((IAccountUsageObserver)observers).ObserveAsync(@"C:\x\codex.exe", null, CancellationToken.None);

        Assert.Same(CodexAccountAllowanceObservation.Unknown, allowance);
        Assert.Same(CodexModelCatalogObservation.Unknown, catalog);
        Assert.Same(AccountUsageObservation.Unavailable, usage);
        // The first call closes the gate with its own reason; the other two are refused by the closed gate, never launched.
        Assert.Equal(1, admission.Facts.Refused["Unlisted"]);
        Assert.Equal(2, admission.Facts.Refused[ProcessAdmission.ClosedCode]);
        Assert.Equal(3, admission.Facts.TotalRefused);
        Assert.Equal("Unlisted", admission.Facts.ClosedBy);
        Assert.Equal(0, admission.Facts.TotalAdmitted);
        Assert.False(Directory.Exists(admission.Ledger.LedgerDirectory));
    }

    [Fact]
    public async Task An_approved_identity_that_is_not_the_launched_file_stops_the_real_flow_at_the_first_probe_before_any_allowance()
    {
        using var rehearsal = await RehearseAsync(approval: set => WithClaudeDigest(set, new string('0', 64)));

        Assert.Equal(QualificationResult.Blocked, rehearsal.Report.Result);
        Assert.Equal("ProcessAdmissionRefused", rehearsal.Report.Code);
        Assert.True(rehearsal.Report.Admission.Refused.ContainsKey("FileIdentityDrift"), rehearsal.Summary);
        Assert.Equal("FileIdentityDrift", rehearsal.Report.Admission.ClosedBy);
        Assert.False(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(rehearsal.Report.Admission.Admitted.ContainsKey("role.codex.exec"));
        Assert.False(rehearsal.Report.Cleanup!.Removed);
        Assert.Equal("InspectionRequired", rehearsal.Report.Cleanup.Reason);
    }

    [Fact]
    public async Task The_host_resolving_another_launch_than_the_approved_tuple_blocks_the_session_with_nothing_spent()
    {
        using var rehearsal = await RehearseAsync(approval: set => WithClaudePath(set, Path.Combine(Path.GetTempPath(), "never-the-claude-double.exe")));

        Assert.NotEqual(QualificationResult.Qualified, rehearsal.Report.Result);
        Assert.False(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(rehearsal.Report.Admission.Admitted.ContainsKey("role.codex.exec"));
    }

    private static ApprovedLaunchSet WithClaudeDigest(ApprovedLaunchSet set, string digest) =>
        Rebuild(set, claude => claude with { Executable = claude.Executable with { Sha256 = digest } });

    private static ApprovedLaunchSet WithClaudePath(ApprovedLaunchSet set, string path) =>
        Rebuild(set, claude => claude with { Executable = claude.Executable with { Path = path } });

    private static ApprovedLaunchSet Rebuild(ApprovedLaunchSet set, Func<ApprovedLaunch, ApprovedLaunch> changeClaude) =>
        ApprovedLaunchSet.TryCreate(
            [.. set.Launches.Select(launch => launch.Capability == ApprovedLaunchSet.Claude ? changeClaude(launch) : launch)]).Set!;

    /// <summary>The approved identities of the rehearsal: the owned doubles installed in the owned root and the real toolchain the
    /// production resolver finds in the same order (this process' search path, then the catalog's fixed directories). Owned stand-ins
    /// for the owner's manifest; nothing here is an installed provider.</summary>
    private static ApprovedLaunchSet RehearsalApprovals(OwnedRootGuard root)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? Locate(string name, params string[] fallbacks)
        {
            var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Concat(fallbacks.Select(fallback => Path.Combine(programFiles, fallback)));
            return directories.Select(directory => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
        }

        ApprovedLaunch? Launch(string capability, string? path) =>
            path is null
                ? null
                : new ApprovedLaunch(
                    capability,
                    new ApprovedFile(
                        Path.GetFullPath(path),
                        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))),
                    null);

        var launches = new[]
        {
            Launch(ApprovedLaunchSet.Git, Locate("git.exe", @"Git\cmd", @"Git\bin")),
            Launch(ApprovedLaunchSet.DotNet, Locate("dotnet.exe", "dotnet")),
            Launch(ApprovedLaunchSet.Node, Locate("node.exe", "nodejs")),
            Launch(ApprovedLaunchSet.GitHub, Path.Combine(root.Bin, "gh.exe")),
            Launch(ApprovedLaunchSet.Docker, Locate("docker.exe", @"Docker\Docker\resources\bin")),
            Launch(ApprovedLaunchSet.Codex, Path.Combine(root.Bin, "codex.exe")),
            Launch(ApprovedLaunchSet.Claude, Path.Combine(root.Bin, "claude.exe")),
        }.OfType<ApprovedLaunch>().ToArray();
        var (set, refusal) = ApprovedLaunchSet.TryCreate(launches);
        return set ?? throw new InvalidOperationException("The rehearsal approvals are incomplete: " + refusal);
    }

    private async Task<Rehearsal> RehearseAsync(
        string? objective = null,
        IReadOnlyDictionary<string, int>? ceilings = null,
        IReadOnlyCollection<string>? withoutClasses = null,
        bool neverPin = false,
        Func<ApprovedLaunchSet, ApprovedLaunchSet>? approval = null,
        IGitVersionObserver? gitVersions = null)
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var table = new ScriptedProcessTable([Explorer, Launcher], [Explorer, Launcher]);
        var judged = new JudgedTargets();
        var (entries, total) = Profile01Inventory.Create(judged);
        var adjusted = entries
            .Where(entry => withoutClasses?.Contains(entry.Name) != true)
            .Select(entry => ceilings is not null && ceilings.TryGetValue(entry.Name, out var limit) ? entry with { Limit = limit } : entry)
            .ToArray();
        var counters = new AdmissionLedger(LedgerDirectory, InvocationLedger.Authorization, TimeProvider.System);
        var admission = new ProcessAdmission(adjusted, total, counters, judged.Revalidate) { LauncherGit = () => judged.LauncherGitPath };
        var environment = new ProductionQualificationEnvironment(
            RehearsalLimits,
            Path.GetTempPath(),
            (root, _) => facts => AcceptOnlyOwnedDoubles(root, facts),
            root =>
            {
                FixtureInstaller.Install(root, AppContext.BaseDirectory);
                Environment.SetEnvironmentVariable("PATH", root.Bin + Path.PathSeparator + originalPath);
                var approved = RehearsalApprovals(root);
                judged.Approve(approval is null ? approved : approval(approved));
            },
            table,
            LauncherId,
            admission,
            neverPin ? null : judged,
            objective,
            gitVersions);

        QualificationReport report;
        try
        {
            await using (environment)
            {
                report = await new QualificationSession(Ledger, environment, RehearsalLimits, "profile01-rehearsal")
                    .RunAsync(CancellationToken.None);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }

        return new Rehearsal(report, SafeSummary.Render(report, environment.ForbiddenFragments), counters, environment.OwnedRoot);
    }

    private static TargetJudgement AcceptOnlyOwnedDoubles(string root, IReadOnlyList<ProviderTargetFact> facts)
    {
        var bin = Path.Combine(root, "bin");
        var reports = new[] { (Capability.CodexCli, "codex", "codex.exe"), (Capability.ClaudeCli, "claude", "claude.exe") }
            .Select(expected =>
            {
                var fact = facts.FirstOrDefault(candidate => candidate.Capability == expected.Item1);
                var owned = fact is { Reason: CapabilityProbeReason.None, LaunchKind: CapabilityLaunchKind.DirectExecutable }
                    && string.Equals(fact.ExecutablePath, Path.Combine(bin, expected.Item3), StringComparison.OrdinalIgnoreCase);
                return new TargetReport(expected.Item2, owned, owned ? "RehearsalDouble" : "NotTheOwnedDouble", fact?.LaunchKind?.ToString(), owned ? fact!.Version : null);
            })
            .ToArray();
        return new TargetJudgement(reports.All(report => report.Real), reports);
    }

    private sealed class Rehearsal(QualificationReport report, string summary, AdmissionLedger counters, OwnedRootReference? root) : IDisposable
    {
        public QualificationReport Report => report;

        public string Summary => summary;

        public AdmissionLedger Counters => counters;

        public void Dispose()
        {
            // Offline fixture teardown of these rehearsals only: the exact root and token the environment held since creation, never a
            // discovered one, and only after the production host stopped and every process of the rehearsal was a short owned double or a
            // completed version probe. A preserved root of a failing rehearsal would otherwise stay in the temp directory.
            if (root is not null && Directory.Exists(root.Path) && report.Shutdown is { HostStopped: true })
            {
                OwnedRootCleaner.Remove(root.Path, root.OwnerToken);
            }
        }
    }
}
