using DevalCopilot.Api.IntegrationTests.ManualQualification;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;
using DevalCopilot.Application.Features.Runs.Ports;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>QP-R2: the admission gate closes atomically with the first refusal or expiry. The first reason and the consumed slots are
/// kept, no later request of any producer is admitted, and a final inspection that becomes unavailable is unproven with the root
/// preserved. Owned stand-in files and owned doubles only.</summary>
public sealed class TerminalAdmissionTests : QualificationTestBase
{
    private ApprovedFixture Fixture() => new(Path.Combine(LedgerDirectory, "approved"));

    private ProcessAdmission Admission(ApprovedFixture fixture, Func<string?>? external = null) =>
        fixture.Admission(Path.Combine(LedgerDirectory, "inventory"), external: external);

    [Fact]
    public void An_unlisted_request_closes_the_gate_and_the_next_valid_request_starts_nothing()
    {
        var fixture = Fixture();
        var admission = Admission(fixture);

        Assert.Equal("Unlisted", Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(new ProcessShape("unknown.exe", []))).Code);
        var later = Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.Probe(fixture.Git, "--version")));

        Assert.Equal(ProcessAdmission.ClosedCode, later.Code);
        Assert.Equal(0, admission.Facts.TotalAdmitted);
        Assert.Equal("Unlisted", admission.Facts.ClosedBy);
        Assert.Equal(1, admission.Facts.Refused["Unlisted"]);
        Assert.Equal(1, admission.Facts.Refused[ProcessAdmission.ClosedCode]);
        Assert.True(admission.IsClosed);
        Assert.False(Directory.Exists(admission.Ledger.LedgerDirectory));
    }

    [Fact]
    public void The_consumed_slots_stay_exactly_as_they_were_when_the_gate_closes()
    {
        var fixture = Fixture();
        var admission = Admission(fixture);
        var probe = fixture.Probe(fixture.Git, "--version");
        admission.Admit(probe);
        admission.Admit(probe);
        var before = admission.Ledger.Consumed().ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.Probe(fixture.Git, "--help")));
        Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(probe));

        Assert.Equal(before, admission.Ledger.Consumed().ToDictionary(pair => pair.Key, pair => pair.Value));
        Assert.Equal(2, admission.Facts.Admitted["discovery.git"]);
        Assert.Equal(2, admission.Facts.TotalAdmitted);
    }

    [Theory]
    [InlineData("ClassLimit")]
    [InlineData("Drift")]
    [InlineData("Observer")]
    public async Task Every_kind_of_refusal_closes_the_gate_with_its_own_first_reason(string kind)
    {
        var fixture = Fixture();
        var admission = Admission(fixture);
        var probe = fixture.Probe(fixture.Git, "--version");
        string expected;
        switch (kind)
        {
            case "ClassLimit":
                for (var index = 0; index < Profile01Inventory.DiscoveryPerCapability; index++)
                {
                    admission.Admit(probe);
                }

                expected = "ClassLimit.discovery.git";
                Assert.Equal(expected, Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(probe)).Code);
                break;
            case "Drift":
                ApprovedFixture.Tamper(fixture.Git);
                expected = "FileIdentityDrift";
                Assert.Equal(expected, Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(probe)).Code);
                break;
            default:
                expected = "Unlisted";
                _ = await ((ICodexAccountAllowanceAdapter)new AdmissionRefusingCodexObservers(admission))
                    .ObserveAsync(fixture.CodexExecutable, null, CancellationToken.None);
                break;
        }

        Assert.Equal(expected, admission.Facts.ClosedBy);
        Assert.Equal(ProcessAdmission.ClosedCode, Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.Probe(fixture.Claude, "--version"))).Code);
        Assert.Equal(expected, admission.Facts.ClosedBy);
    }

    [Fact]
    public async Task An_explicit_close_keeps_its_first_reason_and_blocks_every_later_producer_including_a_background_refresh()
    {
        var fixture = Fixture();
        var admission = Admission(fixture);
        admission.Admit(fixture.Probe(fixture.Git, "--version"));

        admission.Close("First");
        admission.Close("Second");
        var refresh = await Task.Run(() => Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.Probe(fixture.Node, "--version"))));

        Assert.Equal(ProcessAdmission.ClosedCode, refresh.Code);
        Assert.Equal("First", admission.Facts.ClosedBy);
        Assert.Equal(1, admission.Facts.TotalAdmitted);
        Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.CodexRole()));
        Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.Launcher("rev-parse", "HEAD")));
    }

    [Fact]
    public async Task The_three_codex_observers_close_the_gate_on_any_call_and_never_silently_restore_an_observer()
    {
        var fixture = Fixture();
        var admission = Admission(fixture);
        var observers = new AdmissionRefusingCodexObservers(admission);

        _ = await ((ICodexModelCatalogAdapter)observers).ObserveAsync(fixture.CodexExecutable, fixture.CodexScript, CancellationToken.None);

        Assert.True(admission.IsClosed);
        Assert.Equal("Unlisted", admission.Facts.ClosedBy);
        _ = await ((IAccountUsageObserver)observers).ObserveAsync(fixture.CodexExecutable, null, CancellationToken.None);
        Assert.Equal(0, admission.Facts.TotalAdmitted);
        Assert.Equal(ProcessAdmission.ClosedCode, Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.ClaudeRole())).Code);
    }

    [Fact]
    public async Task An_elapsed_entry_deadline_closes_the_gate_at_the_next_decision_whatever_thread_asks()
    {
        var fixture = Fixture();
        var deadline = EntryDeadline.Start(TimeSpan.FromMilliseconds(80));
        var admission = Admission(fixture, () => deadline.IsExpired ? "OuterDeadlineExceeded" : null);
        Assert.Equal("discovery.git", admission.Admit(fixture.Probe(fixture.Git, "--version")));
        await Task.Delay(200);

        var refusal = await Task.Run(() => Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.Probe(fixture.Git, "--version"))));

        Assert.Equal(ProcessAdmission.ClosedCode, refusal.Code);
        Assert.Equal("OuterDeadlineExceeded", admission.Facts.ClosedBy);
        Assert.Equal(1, admission.Facts.Admitted["discovery.git"]);
        Assert.True(admission.IsClosed);
    }

    [Fact]
    public async Task Concurrent_producers_never_admit_a_request_after_the_gate_closed_and_never_lose_a_slot()
    {
        var fixture = Fixture();
        var admission = Admission(fixture);
        using var go = new ManualResetEventSlim();
        var calls = 0;
        var probe = fixture.Probe(fixture.Git, "--version");

        var producers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            go.Wait();
            for (var index = 0; index < 50; index++)
            {
                Interlocked.Increment(ref calls);
                try
                {
                    admission.Admit(probe);
                }
                catch (ProcessAdmissionRefusedException)
                {
                }
            }
        })).ToArray();
        var closer = Task.Run(() =>
        {
            go.Wait();
            Interlocked.Increment(ref calls);
            Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(new ProcessShape("unknown.exe", [])));
        });
        go.Set();
        await Task.WhenAll([.. producers, closer]);

        var facts = admission.Facts;
        // Whichever refusal came first (the unlisted one or the class ceiling) closed the gate and its reason is the one kept.
        Assert.Contains(facts.ClosedBy, new[] { "Unlisted", "ClassLimit.discovery.git" });
        Assert.True(facts.TotalAdmitted <= Profile01Inventory.DiscoveryPerCapability);
        Assert.Equal(calls, facts.TotalAdmitted + facts.TotalRefused);
        Assert.Equal(facts.TotalAdmitted, admission.Ledger.Consumed().GetValueOrDefault("discovery.git"));
        Assert.Equal(facts.TotalAdmitted, admission.Ledger.Consumed().GetValueOrDefault(AdmissionLedger.TotalName));
        for (var attempt = 0; attempt < 20; attempt++)
        {
            Assert.Equal(ProcessAdmission.ClosedCode, Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(probe)).Code);
        }

        Assert.Equal(facts.TotalAdmitted, admission.Facts.TotalAdmitted);
    }

    // The session around a closed gate ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_gate_closed_before_any_allowance_makes_the_final_inspection_required_and_unproven_and_keeps_the_root()
    {
        var environment = new ScriptedEnvironment
        {
            AdmissionFor = calls => calls.Contains("observe")
                ? new ProcessAdmissionFacts(new Dictionary<string, int>(), new Dictionary<string, int> { ["Unlisted"] = 1 }, "Unlisted")
                : ProcessAdmissionFacts.None,
            SourceAt = count => count == 1 ? QualificationReadings.Snapshot() : throw new ProcessAdmissionRefusedException("AdmissionClosed"),
        };

        var report = await RunAsync(environment);

        Assert.Equal("ProcessAdmissionRefused", report.Code);
        Assert.False(report.SourceProven);
        Assert.Contains("SourceUnreadable", report.SourceDifferences);
        Assert.False(report.Cleanup!.Removed);
        Assert.Equal("InspectionRequired", report.Cleanup.Reason);
        Assert.Equal(0, environment.Count("cleanup"));
        Assert.False(Ledger.IsConsumed(AllowanceRole.Planner));
    }

    [Fact]
    public async Task A_gate_closed_after_the_planner_submission_reports_unproven_and_never_cleans_even_with_a_proven_shutdown()
    {
        var environment = new ScriptedEnvironment
        {
            AdmissionFor = calls => calls.Contains("submit.Planner")
                ? new ProcessAdmissionFacts(new Dictionary<string, int>(), new Dictionary<string, int> { ["Unlisted"] = 1 }, "Unlisted")
                : ProcessAdmissionFacts.None,
            SourceAt = count => count <= 2 ? QualificationReadings.Snapshot() : throw new ProcessAdmissionRefusedException("AdmissionClosed"),
            WorkspaceAt = count => count <= 2 ? QualificationReadings.Snapshot("w") : throw new ProcessAdmissionRefusedException("AdmissionClosed"),
        };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal("ProcessAdmissionRefused", report.Code);
        Assert.False(report.SourceProven);
        Assert.False(report.Cleanup!.Removed);
        Assert.Equal(0, environment.Count("cleanup"));
        Assert.Equal(0, environment.Count("submit.Reviewer"));
        Assert.True(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(Ledger.IsConsumed(AllowanceRole.Reviewer));
    }

    [Fact]
    public async Task A_gate_closed_only_by_the_final_inspection_still_disqualifies_an_otherwise_complete_result()
    {
        var environment = new ScriptedEnvironment
        {
            AdmissionFor = calls => calls.Contains("stop")
                ? new ProcessAdmissionFacts(new Dictionary<string, int>(), new Dictionary<string, int>(), "FileIdentityDrift")
                : ProcessAdmissionFacts.None,
        };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal("ProcessAdmissionRefused", report.Code);
        Assert.Null(report.Verdict);
        Assert.False(report.Cleanup!.Removed);
    }
}
