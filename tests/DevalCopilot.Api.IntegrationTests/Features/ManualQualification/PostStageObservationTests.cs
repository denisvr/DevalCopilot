using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>Both repositories are observed after every submitted stage, however it ended. A missing observation is unproven, never
/// unchanged; the first failure is kept; no later stage starts.</summary>
public sealed class PostStageObservationTests : QualificationTestBase
{
    private static ScriptedEnvironment Drifting(Func<ScriptedEnvironment, bool> started, bool source = true, bool workspace = true)
    {
        var environment = new ScriptedEnvironment();
        if (source)
        {
            environment.SourceAt = _ => started(environment) ? QualificationReadings.Snapshot("edited") : QualificationReadings.Snapshot();
        }

        if (workspace)
        {
            environment.WorkspaceAt = _ => started(environment) ? QualificationReadings.Snapshot("edited") : QualificationReadings.Snapshot("w");
        }

        return environment;
    }

    private static void FailPlanner(ScriptedEnvironment environment) =>
        environment.OnRead = (_, _) => QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            with { Status = "Failed", Outcome = "InvalidStructuredOutput", MessageCount = 0, MessageId = null, MessageType = null };

    [Fact]
    public async Task A_failed_planner_that_changed_both_repositories_is_reported_with_its_first_failure_kept()
    {
        var environment = Drifting(e => e.Count("submit.Planner") > 0);
        FailPlanner(environment);

        var report = await RunAsync(environment);

        Assert.Equal("PlannerAttemptFailedInvalidStructuredOutput", report.Code);
        Assert.Contains("Index", report.SourceDifferences);
        Assert.Contains("WorkspaceIndex", report.SourceDifferences);
        Assert.Equal(0, environment.Count("submit.Reviewer"));
        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        Assert.False(summary.RootElement.GetProperty("source").GetProperty("unchanged").GetBoolean());
        Assert.Equal("InspectionRequired", report.Cleanup!.Reason);
    }

    [Fact]
    public async Task A_failed_reviewer_that_changed_a_repository_is_reported_too()
    {
        var environment = Drifting(e => e.Count("submit.Reviewer") > 0, workspace: false);
        environment.OnRead = (role, _) => role == AllowanceRole.Planner
            ? QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            : QualificationReadings.Reviewer(environment.ReviewerAttemptId, environment.ReplyMessageId, environment.ProposalMessageId)
                with { Status = "Failed", Outcome = "ProviderInvocationFailed", MessageCount = 0, MessageId = null };

        var report = await RunAsync(environment);

        Assert.Equal("ReviewerAttemptFailedProviderInvocationFailed", report.Code);
        Assert.Contains("Index", report.SourceDifferences);
    }

    [Fact]
    public async Task A_refused_submission_still_observes_both_repositories()
    {
        var environment = Drifting(e => e.Count("submit.Planner") > 0);
        environment.OnSubmit = (_, _) => Task.FromResult(SubmissionResult.Refused("agent_attempts.workspace_not_ready"));

        var report = await RunAsync(environment);

        Assert.Equal("PlannerSubmissionRefused.agent_attempts.workspace_not_ready", report.Code);
        Assert.Contains("Index", report.SourceDifferences);
        Assert.Contains("WorkspaceIndex", report.SourceDifferences);
    }

    [Fact]
    public async Task An_ambiguous_submission_observes_both_repositories_after_the_host_stopped()
    {
        var environment = Drifting(e => e.Count("stop") > 0);
        environment.OnSubmit = (_, _) => throw new TimeoutException();

        var report = await RunAsync(environment);

        Assert.Equal("PlannerSubmissionAmbiguous", report.Code);
        Assert.Contains("Index", report.SourceDifferences);
        Assert.Contains("WorkspaceIndex", report.SourceDifferences);
        var calls = environment.Calls.ToList();
        Assert.True(calls.IndexOf("stop") < calls.FindLastIndex(call => call == "snapshot.source"));
    }

    [Fact]
    public async Task A_stage_that_hits_its_deadline_is_observed_after_the_host_stopped()
    {
        var environment = Drifting(e => e.Count("stop") > 0);
        environment.OnRead = (_, _) => QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            with { Status = "Running", Outcome = null, MessageCount = 0, MessageId = null, MessageType = null };

        var report = await RunAsync(environment);

        Assert.Equal("PlannerDeadlineBeforeTerminal", report.Code);
        Assert.Contains("Index", report.SourceDifferences);
    }

    [Fact]
    public async Task A_cancelled_session_observes_both_repositories_after_the_host_stopped()
    {
        using var cancellation = new CancellationTokenSource();
        var environment = Drifting(e => e.Count("stop") > 0);
        environment.OnRead = (_, _) =>
        {
            cancellation.Cancel();
            return QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId) with { Status = "Running" };
        };

        var report = await RunAsync(environment, cancellationToken: cancellation.Token);

        Assert.Equal("Cancelled", report.Code);
        Assert.Contains("Index", report.SourceDifferences);
        Assert.Contains("WorkspaceIndex", report.SourceDifferences);
    }

    [Fact]
    public async Task A_qualified_session_whose_repository_changes_after_shutdown_is_not_qualified()
    {
        var environment = Drifting(e => e.Count("stop") > 0);

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal("SourceChanged", report.Code);
        Assert.Null(report.Verdict);
    }

    [Fact]
    public async Task An_unreadable_source_is_unproven_and_does_not_suppress_the_workspace_observation()
    {
        var environment = Drifting(e => e.Count("submit.Planner") > 0, source: false);
        environment.SourceAt = call => environment.Count("submit.Planner") > 0
            ? throw new InvalidOperationException("git failed")
            : QualificationReadings.Snapshot();
        FailPlanner(environment);

        var report = await RunAsync(environment);

        Assert.Equal("PlannerAttemptFailedInvalidStructuredOutput", report.Code);
        Assert.Contains("SourceUnreadable", report.SourceDifferences);
        Assert.Contains("WorkspaceIndex", report.SourceDifferences);
        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        Assert.False(summary.RootElement.GetProperty("source").GetProperty("unchanged").GetBoolean());
        Assert.False(summary.RootElement.GetProperty("source").GetProperty("proven").GetBoolean());
    }

    [Fact]
    public async Task An_unreadable_workspace_is_unproven_and_does_not_suppress_the_source_observation()
    {
        var environment = Drifting(e => e.Count("submit.Planner") > 0, workspace: false);
        environment.WorkspaceAt = _ => environment.Count("submit.Planner") > 0
            ? throw new IOException("worktree gone")
            : QualificationReadings.Snapshot("w");
        FailPlanner(environment);

        var report = await RunAsync(environment);

        Assert.Contains("WorkspaceUnreadable", report.SourceDifferences);
        Assert.Contains("Index", report.SourceDifferences);
        Assert.False(report.SourceProven);
    }

    [Fact]
    public async Task A_qualified_session_with_an_unreadable_final_observation_is_not_qualified()
    {
        var environment = new ScriptedEnvironment();
        environment.SourceAt = _ => environment.Count("stop") > 0
            ? throw new InvalidOperationException("git failed")
            : QualificationReadings.Snapshot();

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal("SourceUnreadable", report.Code);
    }

    [Fact]
    public async Task An_unchanged_failure_and_an_unchanged_success_are_both_proven_unchanged()
    {
        var failed = new ScriptedEnvironment();
        FailPlanner(failed);
        var failedReport = await RunAsync(failed);
        var ok = await new QualificationSession(
                new InvocationLedger(Path.Combine(LedgerDirectory, "ok"), TimeProvider.System), new ScriptedEnvironment(), Tiny, "ok-session")
            .RunAsync(CancellationToken.None);

        foreach (var report in new[] { failedReport, ok })
        {
            Assert.Empty(report.SourceDifferences);
            Assert.True(report.SourceProven);
            using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
            Assert.True(summary.RootElement.GetProperty("source").GetProperty("unchanged").GetBoolean());
            Assert.True(summary.RootElement.GetProperty("source").GetProperty("proven").GetBoolean());
        }

        Assert.Equal(QualificationResult.Qualified, ok.Result);
    }

    [Fact]
    public void A_submitted_stage_that_was_never_observed_afterwards_is_unproven_even_when_nothing_differs()
    {
        var report = new QualificationReport("report-only") { PlannerSubmitted = true };
        report.NoteObservation([], complete: true, afterSubmission: false);

        Assert.False(report.SourceProven);
        using (var before = JsonDocument.Parse(SafeSummary.Render(report, [])))
        {
            var source = before.RootElement.GetProperty("source");
            Assert.False(source.GetProperty("proven").GetBoolean());
            Assert.False(source.GetProperty("unchanged").GetBoolean());
        }

        report.NoteObservation([], complete: true, afterSubmission: true);

        Assert.True(report.SourceProven);
        using var after = JsonDocument.Parse(SafeSummary.Render(report, []));
        Assert.True(after.RootElement.GetProperty("source").GetProperty("unchanged").GetBoolean());
    }

    [Fact]
    public async Task A_blocked_session_that_submitted_nothing_makes_no_post_stage_observation()
    {
        var environment = new ScriptedEnvironment
        {
            Targets = new TargetJudgement(false, [new TargetReport("codex", false, "NotObservedExecutableNotFound", null, null)]),
        };

        var report = await RunAsync(environment);

        Assert.Equal(1, environment.Count("snapshot.source"));
        Assert.Equal(0, environment.Count("snapshot.workspace"));
        Assert.Equal(0, report.SourceChecks);
    }
}
