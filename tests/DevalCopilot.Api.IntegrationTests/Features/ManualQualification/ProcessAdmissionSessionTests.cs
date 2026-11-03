using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>What the session does when the process admission boundary refuses a request: it never proceeds, retries, relabels or
/// refunds, and it reports the closed counts.</summary>
public sealed class ProcessAdmissionSessionTests : QualificationTestBase
{
    private static ProcessAdmissionFacts Refused(string code, int admitted = 0) => new(
        new Dictionary<string, int> { ["discovery.git"] = admitted },
        new Dictionary<string, int> { [code] = 1 });

    [Fact]
    public async Task A_refusal_before_any_allowance_stops_the_session_blocked_with_both_allowances_unspent()
    {
        var environment = new ScriptedEnvironment
        {
            AdmissionFor = calls => calls.Contains("observe") ? Refused("Unlisted") : ProcessAdmissionFacts.None,
        };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Blocked, report.Result);
        Assert.Equal("ProcessAdmissionRefused", report.Code);
        Assert.Equal(0, environment.Count("submit.Planner"));
        Assert.False(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(Ledger.IsConsumed(AllowanceRole.Reviewer));
        Assert.Equal(1, report.Admission.TotalRefused);
    }

    [Fact]
    public async Task A_refusal_after_the_planner_submission_stops_before_the_reviewer_and_keeps_the_spent_allowance()
    {
        var environment = new ScriptedEnvironment
        {
            AdmissionFor = calls => calls.Contains("submit.Planner") ? Refused("ClassLimit.discovery.node", 8) : ProcessAdmissionFacts.None,
        };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal("ProcessAdmissionRefused", report.Code);
        Assert.Equal(1, environment.Count("submit.Planner"));
        Assert.Equal(0, environment.Count("submit.Reviewer"));
        Assert.True(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(Ledger.IsConsumed(AllowanceRole.Reviewer));
    }

    [Fact]
    public async Task A_refusal_while_waiting_for_the_terminal_attempt_ends_the_wait_without_a_further_submission()
    {
        var environment = new ScriptedEnvironment
        {
            AdmissionFor = calls => calls.Count(call => call == "read.Planner") >= 2 ? Refused("Unlisted") : ProcessAdmissionFacts.None,
        };
        environment.OnRead = (_, _) => QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            with
        {
            Status = "Running",
            Outcome = null,
            MessageCount = null,
            MessageId = null,
            MessageType = null,
            EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted,
        };

        var report = await RunAsync(environment);

        Assert.Equal("ProcessAdmissionRefused", report.Code);
        Assert.Equal(0, environment.Count("submit.Reviewer"));
    }

    [Fact]
    public async Task A_refusal_only_after_both_stages_disqualifies_an_otherwise_coherent_result()
    {
        var environment = new ScriptedEnvironment
        {
            AdmissionFor = calls => calls.Contains("stop") ? Refused("Unlisted") : ProcessAdmissionFacts.None,
        };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal("ProcessAdmissionRefused", report.Code);
        Assert.Null(report.Verdict);
        Assert.False(report.Cleanup!.Removed);
    }

    [Fact]
    public async Task A_refused_process_during_fixture_creation_is_a_closed_blocked_code_with_nothing_spent()
    {
        var environment = new ScriptedEnvironment { PrepareFailure = new ProcessAdmissionRefusedException("Unlisted") };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Blocked, report.Result);
        Assert.Equal("ProcessAdmissionRefused", report.Code);
        Assert.Equal(0, environment.Count("start"));
        Assert.False(Ledger.IsSpent());
    }

    [Fact]
    public async Task A_clean_session_reports_zero_refusals_and_the_admitted_counts_in_the_summary()
    {
        var environment = new ScriptedEnvironment
        {
            AdmissionFor = _ => new ProcessAdmissionFacts(
                new Dictionary<string, int> { ["role.codex.exec"] = 1, ["role.claude.print"] = 1 },
                new Dictionary<string, int>()),
        };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Qualified, report.Result);
        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        var admission = summary.RootElement.GetProperty("processAdmission");
        Assert.Equal(2, admission.GetProperty("totalAdmitted").GetInt32());
        Assert.Equal(0, admission.GetProperty("totalRefused").GetInt32());
        Assert.Equal(1, admission.GetProperty("admitted").GetProperty("role.codex.exec").GetInt32());
        Assert.Equal(InvocationLedger.Authorization, summary.RootElement.GetProperty("authorization").GetString());
    }

    [Fact]
    public async Task A_refusal_code_with_hostile_text_never_reaches_the_summary()
    {
        var environment = new ScriptedEnvironment
        {
            AdmissionFor = calls => calls.Contains("stop") ? Refused("C:/Users/someone/secret --password hunter2") : ProcessAdmissionFacts.None,
        };

        var report = await RunAsync(environment);
        var text = SafeSummary.Render(report, []);

        Assert.DoesNotContain("someone", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        using var _ = JsonDocument.Parse(text);
    }

    [Fact]
    public async Task A_second_launch_over_a_spent_authorization_creates_no_environment_effect_at_all()
    {
        var first = new ScriptedEnvironment();
        await RunAsync(first, "first");
        var second = new ScriptedEnvironment();

        var report = await RunAsync(second, "second");

        Assert.Equal(QualificationResult.SessionSpent, report.Result);
        Assert.Empty(second.Calls);
    }

    [Fact]
    public async Task A_process_slot_alone_already_consumed_makes_the_authorization_spent_with_no_rearm()
    {
        var counters = new AdmissionLedger(LedgerDirectory, InvocationLedger.Authorization, TimeProvider.System);
        Assert.Null(counters.TryAdmit("discovery.git", 8, 179));
        var environment = new ScriptedEnvironment();

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.SessionSpent, report.Result);
        Assert.Empty(environment.Calls);
        Assert.False(Ledger.IsConsumed(AllowanceRole.Planner));
    }
}
