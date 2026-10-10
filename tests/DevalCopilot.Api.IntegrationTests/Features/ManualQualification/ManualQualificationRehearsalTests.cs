using DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;
using Xunit.Abstractions;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>
/// A rehearsal of the manual session through the real composition and every launcher component (production host on Kestrel, the
/// authenticated routes, read-only evidence, Git snapshots, the ledger and the owned-root cleanup), with only the provider
/// executables replaced by the journey's deterministic doubles. It proves the launcher works end to end offline. It is not a provider
/// result, and the launcher itself can never be given these doubles: only this test supplies the judge that accepts them. The
/// controlled cases also supply a scripted, read-only process table (an offline observation input, never native shutdown evidence)
/// so that the cleanup decision does not depend on the processes that happen to run on the machine; one case keeps the native
/// table and asserts exactly the safe outcomes the accepted child-process policy allows.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ManualQualificationRehearsalTests(ITestOutputHelper output) : QualificationTestBase
{
    private const int LauncherId = 100;
    private static readonly DateTime Epoch = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

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

    [Fact]
    public async Task A_controlled_complete_observation_authorizes_cleanup_of_only_the_owned_root()
    {
        var table = new ScriptedProcessTable([Explorer, Launcher], [Explorer, Launcher]);

        using var rehearsal = await RehearseAsync(table, LauncherId);

        rehearsal.AssertQualifiedThroughTheOwnedDoubles(Ledger);
        var shutdown = rehearsal.Report.Shutdown!;
        Assert.True(shutdown.IsProven, rehearsal.Summary);
        Assert.Equal("Proven", shutdown.Reason);
        Assert.True(rehearsal.Report.Cleanup!.Removed, rehearsal.Summary);
        Assert.Equal(0, rehearsal.Report.ExitCode);
        Assert.False(Directory.Exists(rehearsal.OwnedRoot.Path), rehearsal.Summary);
        rehearsal.AssertOtherRootsUntouched();
        Assert.True(table.Reads > 2, "The scripted table was read by the baseline, the sampler and the final proof.");
    }

    [Fact]
    public async Task A_controlled_unknown_origin_observation_preserves_the_root_reports_its_reason_and_exits_6()
    {
        var unattributed = new ProcessEntry(9999, 8888, "unattributed-helper.exe", Epoch.AddMinutes(9));
        var table = new ScriptedProcessTable([Explorer, Launcher], [Explorer, Launcher, unattributed]);

        using var rehearsal = await RehearseAsync(table, LauncherId);

        rehearsal.AssertQualifiedThroughTheOwnedDoubles(Ledger);
        var shutdown = rehearsal.Report.Shutdown!;
        Assert.True(shutdown.HostStopped, rehearsal.Summary);
        Assert.False(shutdown.ChildrenProven, rehearsal.Summary);
        Assert.Equal("OriginUnresolved", shutdown.Reason);
        Assert.Equal("ShutdownUnproven", rehearsal.Report.Cleanup!.Reason);
        Assert.False(rehearsal.Report.Cleanup.Removed, rehearsal.Summary);
        Assert.Equal(6, rehearsal.Report.ExitCode);
        rehearsal.AssertOtherRootsUntouched();
        rehearsal.AssertOwnedRootPreserved();
        using var summary = System.Text.Json.JsonDocument.Parse(rehearsal.Summary);
        Assert.Equal("OriginUnresolved", summary.RootElement.GetProperty("shutdown").GetProperty("childProcessProof").GetString());

        // Offline fixture teardown of this one scripted case only: the exact root and token held since creation, and only
        // because the host stopped and both doubles were measured to have exited. Never reached for a native observation.
        var removed = ScriptedFixtureTeardown.TryRemove(
            rehearsal.OwnedRoot, rehearsal.Report, scriptedObservation: true, OwnedRootCleaner.Remove);
        Assert.NotNull(removed);
        Assert.True(removed.Removed, removed.Reason);
        Assert.False(Directory.Exists(rehearsal.OwnedRoot.Path));
    }

    [Fact]
    public async Task The_native_process_table_ends_in_exactly_one_of_the_two_safe_outcomes_of_the_accepted_policy()
    {
        using var rehearsal = await RehearseAsync(table: null, launcherProcessId: null);

        rehearsal.AssertQualifiedThroughTheOwnedDoubles(Ledger);
        var shutdown = rehearsal.Report.Shutdown!;
        output.WriteLine($"Native shutdown facts: hostStopped={shutdown.HostStopped} childrenProven={shutdown.ChildrenProven} reason={shutdown.Reason}");
        Assert.True(shutdown.HostStopped, rehearsal.Summary);
        rehearsal.AssertOtherRootsUntouched();
        if (shutdown.IsProven)
        {
            Assert.Equal("Proven", shutdown.Reason);
            Assert.True(rehearsal.Report.Cleanup!.Removed, rehearsal.Summary);
            Assert.Equal(0, rehearsal.Report.ExitCode);
            Assert.False(Directory.Exists(rehearsal.OwnedRoot.Path), rehearsal.Summary);
            return;
        }

        // The accepted policy preserves the root when ambient ancestry or identity is uncertain. A known owned leftover
        // (LeftoverAlive) or a failed table read (EnumerationFailed) is never an accepted outcome here.
        Assert.Contains(shutdown.Reason, new[] { "OriginUnresolved", "IdentityUnavailable" });
        Assert.Equal("ShutdownUnproven", rehearsal.Report.Cleanup!.Reason);
        Assert.False(rehearsal.Report.Cleanup.Removed, rehearsal.Summary);
        Assert.Equal(6, rehearsal.Report.ExitCode);

        // The exact owned root stays preserved. Nothing in this test, its disposal or a finally block removes it: knowing the
        // owner of a root does not prove that an unresolved native process stopped. It is an intentionally retained fixture.
        rehearsal.AssertOwnedRootPreserved();
    }

    /// <summary>A snapshot used only to assert isolation. It never selects a root to remove.</summary>
    private static string[] ManualRoots() =>
        Directory.GetDirectories(Path.GetTempPath(), OwnedRootGuard.RootPrefix + "manual-*");

    private async Task<Rehearsal> RehearseAsync(IProcessTable? table, int? launcherProcessId)
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var rootsBefore = ManualRoots();
        var foreign = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), OwnedRootGuard.RootPrefix + "manual-xunit-foreign-" + Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(foreign, "sentinel.txt"), "unchanged");
        TargetJudgement? realJudgement = null;
        var environment = new ProductionQualificationEnvironment(
            RehearsalLimits,
            Path.GetTempPath(),
            (root, launcher) => facts =>
            {
                realJudgement = new LaunchTargetJudge(root, launcher).Judge(facts);
                return AcceptOnlyOwnedDoubles(root, facts);
            },
            root =>
            {
                FixtureInstaller.Install(root, AppContext.BaseDirectory);
                Environment.SetEnvironmentVariable("PATH", root.Bin + Path.PathSeparator + originalPath);
            },
            table,
            launcherProcessId);

        QualificationReport report;
        try
        {
            await using (environment)
            {
                report = await new QualificationSession(Ledger, environment, RehearsalLimits, "rehearsal-session")
                    .RunAsync(CancellationToken.None);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }

        return new Rehearsal(
            report,
            SafeSummary.Render(report, environment.ForbiddenFragments),
            realJudgement,
            originalPath,
            rootsBefore,
            foreign,
            environment.OwnedRoot);
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

    /// <summary>One finished rehearsal: its report, its allowlisted safe summary (the diagnostic message of every assertion about
    /// it, free of secrets, paths and repository content), the exact root its environment created, and the isolation facts.</summary>
    private sealed class Rehearsal(
        QualificationReport report,
        string summary,
        TargetJudgement? realJudgement,
        string? originalPath,
        string[] rootsBefore,
        string foreign,
        OwnedRootReference? ownedRoot) : IDisposable
    {
        public QualificationReport Report => report;

        public string Summary => summary;

        public string[] RootsBefore => rootsBefore;

        public string Foreign => foreign;

        public void Dispose()
        {
            if (Directory.Exists(foreign))
            {
                Directory.Delete(foreign, recursive: true);
            }
        }

        public void AssertQualifiedThroughTheOwnedDoubles(InvocationLedger ledger)
        {
            Assert.True(report.Qualified, summary);
            Assert.Equal("Challenge", report.Verdict);
            Assert.Equal("Codex", report.Planner!.Provider);
            Assert.Equal("Planner", report.Planner.Role);
            Assert.Equal("Proposal", report.Planner.MessageType);
            Assert.Equal("ClaudeCode", report.Reviewer!.Provider);
            Assert.Equal(report.Planner.MessageId, report.Reviewer.InReplyToMessageId);
            Assert.Equal([report.Planner.MessageId!.Value], report.Reviewer.InputMessageIds);
            Assert.True(report.Reviewer.ManifestBytesAgree && report.Planner.ManifestBytesAgree, summary);
            Assert.Equal(report.Planner.ArtifactCount, report.Planner.ArtifactsAgreeing);
            Assert.True(report.SourceChecks >= 3, summary);
            Assert.Empty(report.SourceDifferences);
            Assert.True(report.SourceProven, summary);
            Assert.True(ledger.IsConsumed(AllowanceRole.Planner) && ledger.IsConsumed(AllowanceRole.Reviewer));
            Assert.False(realJudgement!.Accepted);
            Assert.All(realJudgement.Targets, target => Assert.Equal("FixtureLocation", target.Code));
            Assert.Equal(originalPath, Environment.GetEnvironmentVariable("PATH"));
        }

        /// <summary>The exact root this rehearsal's environment created, held from its creation. It is never discovered afterwards.</summary>
        public OwnedRootReference OwnedRoot => ownedRoot ?? throw new InvalidOperationException("The environment created no root.");

        /// <summary>The root is physically present with its ownership marker. Only its presence is read, never its token.</summary>
        public void AssertOwnedRootPreserved()
        {
            Assert.True(Directory.Exists(OwnedRoot.Path), summary);
            Assert.True(File.Exists(Path.Combine(OwnedRoot.Path, OwnedRootGuard.MarkerFile)), summary);
        }

        /// <summary>Isolation only, never authority to delete: the foreign sentinel and every root that existed before the run are
        /// still there. A root that appeared meanwhile is neither inspected nor claimed.</summary>
        public void AssertOtherRootsUntouched()
        {
            Assert.Equal("unchanged", File.ReadAllText(Path.Combine(foreign, "sentinel.txt")));
            Assert.All(rootsBefore, root => Assert.True(Directory.Exists(root), "A root that existed before the rehearsal is gone."));
        }
    }
}