using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>Allowance consumption, POST submission, dispatch, measured execution and uncertainty are separate facts. Only
/// host-measured process evidence proves an observed invocation; a dispatch marker alone proves nothing about a process.</summary>
public sealed class InvocationEvidenceTests : QualificationTestBase
{
    private async Task<JsonElement> PlannerAllowanceAfter(Func<ScriptedEnvironment, ScriptedEnvironment> arrange)
    {
        var environment = arrange(new ScriptedEnvironment());
        var report = await RunAsync(environment);
        var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        return summary.RootElement.GetProperty("allowances").GetProperty("planner").Clone();
    }

    private static ScriptedEnvironment Reading(ScriptedEnvironment environment, Func<StageReading, StageReading> change)
    {
        environment.OnRead = (_, _) => change(QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId));
        return environment;
    }

    [Fact]
    public async Task A_dispatched_failure_without_process_evidence_is_not_an_observed_invocation()
    {
        var planner = await PlannerAllowanceAfter(environment => Reading(environment, reading => reading with
        {
            Status = "Failed",
            Outcome = "ProviderInvocationFailed",
            Dispatched = true,
            ProcessOutcome = null,
            ExitCode = null,
            DurationMilliseconds = null,
            MessageCount = 0,
            MessageId = null,
            MessageType = null,
        }));

        Assert.True(planner.GetProperty("consumedBeforePost").GetBoolean());
        Assert.True(planner.GetProperty("postAttempted").GetBoolean());
        Assert.True(planner.GetProperty("dispatched").GetBoolean());
        Assert.False(planner.GetProperty("executionObserved").GetBoolean());
        Assert.Equal("DispatchedNoExecutionEvidence", planner.GetProperty("invocationState").GetString());
    }

    [Fact]
    public async Task A_running_dispatched_attempt_is_not_yet_an_observed_execution()
    {
        var planner = await PlannerAllowanceAfter(environment => Reading(environment, reading => reading with
        {
            Status = "Running",
            Outcome = null,
            ProcessOutcome = null,
            ExitCode = null,
            MessageCount = 0,
            MessageId = null,
            MessageType = null,
        }));

        Assert.False(planner.GetProperty("executionObserved").GetBoolean());
        Assert.Equal("DispatchedNoExecutionEvidence", planner.GetProperty("invocationState").GetString());
    }

    [Theory]
    [InlineData("Exited", 0, true)]
    [InlineData("Exited", 3, true)]
    [InlineData("TimedOut", null, true)]
    [InlineData("Cancelled", null, false)]
    [InlineData("Exited", null, false)]
    public async Task Only_measured_process_evidence_proves_an_execution(string outcome, int? exitCode, bool observed)
    {
        var planner = await PlannerAllowanceAfter(environment => Reading(environment, reading => reading with
        {
            Status = "Failed",
            Outcome = "ProviderInvocationFailed",
            ProcessOutcome = outcome,
            ExitCode = exitCode,
            MessageCount = 0,
            MessageId = null,
            MessageType = null,
        }));

        Assert.Equal(observed, planner.GetProperty("executionObserved").GetBoolean());
        Assert.Equal(observed ? "ExecutionObserved" : "DispatchedNoExecutionEvidence", planner.GetProperty("invocationState").GetString());
    }

    [Fact]
    public async Task A_successful_measured_stage_is_an_observed_execution()
    {
        var planner = await PlannerAllowanceAfter(environment => environment);

        Assert.True(planner.GetProperty("executionObserved").GetBoolean());
        Assert.Equal("ExecutionObserved", planner.GetProperty("invocationState").GetString());
    }

    [Fact]
    public async Task A_pre_dispatch_refusal_is_a_spent_allowance_without_any_invocation_claim()
    {
        var planner = await PlannerAllowanceAfter(environment =>
        {
            environment.OnSubmit = (_, _) => Task.FromResult(SubmissionResult.Refused("agent_attempts.provider_not_observed"));
            return environment;
        });

        Assert.True(planner.GetProperty("consumedBeforePost").GetBoolean());
        Assert.False(planner.GetProperty("dispatched").GetBoolean());
        Assert.False(planner.GetProperty("executionObserved").GetBoolean());
        Assert.Equal("RefusedBeforeDispatch", planner.GetProperty("invocationState").GetString());
    }

    [Fact]
    public async Task An_ambiguous_submission_with_no_reading_is_unknown_not_observed_and_not_denied()
    {
        var planner = await PlannerAllowanceAfter(environment =>
        {
            environment.OnSubmit = (_, _) => throw new TimeoutException();
            environment.OnRead = (_, _) => throw new InvalidOperationException();
            return environment;
        });

        Assert.True(planner.GetProperty("postAttempted").GetBoolean());
        Assert.False(planner.GetProperty("executionObserved").GetBoolean());
        Assert.Equal("Unknown", planner.GetProperty("invocationState").GetString());
    }

    [Fact]
    public async Task An_attempt_that_was_never_dispatched_says_so_without_claiming_no_process_ran()
    {
        var planner = await PlannerAllowanceAfter(environment => Reading(environment, reading => reading with
        {
            Status = "Failed",
            Outcome = "WorkspaceNoLongerEligible",
            Dispatched = false,
            ProcessOutcome = null,
            ExitCode = null,
            MessageCount = 0,
            MessageId = null,
            MessageType = null,
        }));

        Assert.False(planner.GetProperty("executionObserved").GetBoolean());
        Assert.Equal("NotDispatched", planner.GetProperty("invocationState").GetString());
    }

    [Fact]
    public async Task A_stage_that_was_never_posted_is_not_attempted()
    {
        var environment = new ScriptedEnvironment
        {
            Targets = new TargetJudgement(false, [new TargetReport("codex", false, "NotObservedExecutableNotFound", null, null)]),
        };
        var report = await RunAsync(environment);

        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        var reviewer = summary.RootElement.GetProperty("allowances").GetProperty("reviewer");

        Assert.Equal("NotAttempted", reviewer.GetProperty("invocationState").GetString());
        Assert.False(reviewer.GetProperty("executionObserved").GetBoolean());
    }

    [Fact]
    public async Task The_may_have_run_handling_stays_conservative_when_execution_is_unproven()
    {
        var environment = new ScriptedEnvironment();
        Reading(environment, reading => reading with
        {
            Status = "Failed",
            Outcome = "ProviderInvocationFailed",
            ProcessOutcome = null,
            ExitCode = null,
            MessageCount = 0,
            MessageId = null,
            MessageType = null,
        });

        var report = await RunAsync(environment);

        Assert.True(report.ProviderMayHaveRun);
        Assert.Equal("InspectionRequired", report.Cleanup!.Reason);
        Assert.Equal(1, environment.Count("submit.Planner"));
    }
}
