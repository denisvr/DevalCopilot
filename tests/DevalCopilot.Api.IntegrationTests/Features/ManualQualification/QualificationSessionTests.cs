using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>
/// The controls around the two real provider invocations, proven with a scripted stand-in for everything outside the session. None of
/// these is a provider result: they prove what the launcher would do, and refuse to do, around a real one.
/// </summary>
public sealed class QualificationSessionTests : QualificationTestBase
{
    [Fact]
    public async Task A_challenge_replying_to_the_exact_proposal_qualifies_without_an_acceptance_being_forced()
    {
        var environment = new ScriptedEnvironment();

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Qualified, report.Result);
        Assert.Equal("Challenge", report.Verdict);
        Assert.Equal(1, environment.Count("submit.Planner"));
        Assert.Equal(1, environment.Count("submit.Reviewer"));
        Assert.True(Ledger.IsConsumed(AllowanceRole.Planner) && Ledger.IsConsumed(AllowanceRole.Reviewer));
        var calls = environment.Calls.ToList();
        Assert.True(calls.IndexOf("prepare") < calls.IndexOf("start") && calls.IndexOf("start") < calls.IndexOf("observe"));
        Assert.True(calls.IndexOf("observe") < calls.IndexOf("setup") && calls.IndexOf("setup") < calls.IndexOf("submit.Planner"));
        Assert.True(calls.IndexOf("submit.Planner") < calls.IndexOf("submit.Reviewer"));
        Assert.True(calls.IndexOf("submit.Reviewer") < calls.IndexOf("stop") && calls.IndexOf("stop") < calls.IndexOf("cleanup"));
        Assert.True(report.Cleanup!.Removed);
        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public async Task An_acceptance_also_qualifies()
    {
        var environment = new ScriptedEnvironment();
        environment.OnRead = (role, _) => role == AllowanceRole.Planner
            ? QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            : QualificationReadings.Reviewer(environment.ReviewerAttemptId, environment.ReplyMessageId, environment.ProposalMessageId, accepted: true);

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Qualified, report.Result);
        Assert.Equal("Acceptance", report.Verdict);
    }

    [Fact]
    public async Task Each_allowance_is_consumed_before_its_one_post()
    {
        var environment = new ScriptedEnvironment();
        var seenAtSubmit = new List<(AllowanceRole Role, bool Planner, bool Reviewer)>();
        environment.OnSubmit = (role, _) =>
        {
            seenAtSubmit.Add((role, Ledger.IsConsumed(AllowanceRole.Planner), Ledger.IsConsumed(AllowanceRole.Reviewer)));
            return Task.FromResult(SubmissionResult.Accepted(role == AllowanceRole.Planner ? environment.PlannerAttemptId : environment.ReviewerAttemptId));
        };

        await RunAsync(environment);

        Assert.Equal(
            new[] { (AllowanceRole.Planner, true, false), (AllowanceRole.Reviewer, true, true) },
            seenAtSubmit.ToArray());
    }

    [Fact]
    public async Task A_spent_ledger_starts_nothing_at_all()
    {
        Assert.Equal(ConsumeOutcome.Consumed, Ledger.Consume(AllowanceRole.Planner, "an-earlier-session"));
        var environment = new ScriptedEnvironment();

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.SessionSpent, report.Result);
        Assert.Empty(environment.Calls);
        Assert.Equal(3, report.ExitCode);
    }

    [Fact]
    public async Task Targets_that_are_not_real_block_before_any_allowance_is_spent()
    {
        var environment = new ScriptedEnvironment
        {
            Targets = new TargetJudgement(false, [new TargetReport("codex", false, "FixtureLocation", "DirectExecutable", null)]),
        };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Blocked, report.Result);
        Assert.Equal("TargetsNotReal", report.Code);
        Assert.False(Ledger.IsSpent());
        Assert.Equal(0, environment.Count("submit.Planner") + environment.Count("submit.Reviewer"));
        Assert.Equal(1, environment.Count("stop"));
        Assert.Equal(1, environment.Count("cleanup"));
        Assert.True(report.Cleanup!.Removed);
        Assert.Equal(4, report.ExitCode);
    }

    [Fact]
    public async Task An_ambiguous_planner_submission_is_never_repeated_and_starts_no_review()
    {
        var environment = new ScriptedEnvironment { OnSubmit = (_, _) => throw new TimeoutException() };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal("PlannerSubmissionAmbiguous", report.Code);
        Assert.Equal(1, environment.Count("submit.Planner"));
        Assert.Equal(0, environment.Count("submit.Reviewer"));
        Assert.Equal(1, environment.Count("read.Planner"));
        Assert.True(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(Ledger.IsConsumed(AllowanceRole.Reviewer));
        Assert.Equal(0, environment.Count("cleanup"));
        Assert.Equal("InspectionRequired", report.Cleanup!.Reason);
    }

    [Fact]
    public async Task A_cancelled_submission_is_ambiguous_and_is_not_repeated()
    {
        var environment = new ScriptedEnvironment { OnSubmit = (_, _) => throw new OperationCanceledException() };

        var report = await RunAsync(environment);

        Assert.Equal("PlannerSubmissionAmbiguous", report.Code);
        Assert.Equal(1, environment.Count("submit.Planner"));
    }

    [Fact]
    public async Task A_refused_planner_submission_stays_spent_and_is_not_retried()
    {
        var environment = new ScriptedEnvironment
        {
            OnSubmit = (_, _) => Task.FromResult(SubmissionResult.Refused("agent_attempts.provider_not_observed")),
        };

        var report = await RunAsync(environment);

        Assert.Equal("PlannerSubmissionRefused.agent_attempts.provider_not_observed", report.Code);
        Assert.Equal(1, environment.Count("submit.Planner"));
        Assert.Equal(0, environment.Count("read.Planner"));
        Assert.True(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(Ledger.IsConsumed(AllowanceRole.Reviewer));
    }

    [Fact]
    public async Task A_planner_that_never_finishes_ends_at_its_deadline_without_a_second_stage()
    {
        var environment = new ScriptedEnvironment();
        environment.OnRead = (_, _) => QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            with { Status = "Running", Outcome = null, MessageCount = 0, MessageId = null, MessageType = null };

        var report = await RunAsync(environment);

        Assert.Equal("PlannerDeadlineBeforeTerminal", report.Code);
        Assert.Equal(0, environment.Count("submit.Reviewer"));
        Assert.False(Ledger.IsConsumed(AllowanceRole.Reviewer));
        Assert.Equal("InspectionRequired", report.Cleanup!.Reason);
    }

    [Fact]
    public async Task Repeated_read_failures_are_bounded_and_end_the_session_without_a_resubmission()
    {
        var environment = new ScriptedEnvironment { OnRead = (_, _) => throw new InvalidOperationException() };

        var report = await RunAsync(environment);

        Assert.Equal("PlannerDeadlineBeforeTerminal", report.Code);
        Assert.Equal(3, environment.Count("read.Planner"));
        Assert.Equal(1, environment.Count("submit.Planner"));
    }

    [Theory]
    [InlineData("Failed", "InvalidStructuredOutput", "PlannerAttemptFailedInvalidStructuredOutput")]
    [InlineData("Failed", "ProviderInvocationFailed", "PlannerAttemptFailedProviderInvocationFailed")]
    [InlineData("Interrupted", null, "PlannerAttemptInterruptedNone")]
    [InlineData("Completed", "SourceChanged", "PlannerAttemptCompletedSourceChanged")]
    public async Task A_planner_that_does_not_record_a_proposal_leaves_the_reviewer_allowance_unspent(string status, string? outcome, string code)
    {
        var environment = new ScriptedEnvironment();
        environment.OnRead = (_, _) => QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            with { Status = status, Outcome = outcome, MessageCount = 0, MessageId = null, MessageType = null };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal(code, report.Code);
        Assert.Equal(0, environment.Count("submit.Reviewer"));
        Assert.False(Ledger.IsConsumed(AllowanceRole.Reviewer));
    }

    [Fact]
    public async Task A_changed_source_after_the_planner_stops_before_the_reviewer_allowance_is_consumed()
    {
        var environment = new ScriptedEnvironment();
        environment.SourceAt = _ => environment.Count("submit.Planner") > 0 ? QualificationReadings.Snapshot("edited") : QualificationReadings.Snapshot();

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal("SourceChanged", report.Code);
        Assert.Equal(0, environment.Count("submit.Reviewer"));
        Assert.False(Ledger.IsConsumed(AllowanceRole.Reviewer));
        Assert.Contains("Index", report.SourceDifferences);
        Assert.Equal("InspectionRequired", report.Cleanup!.Reason);
    }

    [Fact]
    public async Task A_changed_prepared_workspace_is_a_source_change_too()
    {
        var environment = new ScriptedEnvironment();
        environment.WorkspaceAt = _ => environment.Count("submit.Planner") > 0 ? QualificationReadings.Snapshot("edited") : QualificationReadings.Snapshot("w");

        var report = await RunAsync(environment);

        Assert.Equal("SourceChanged", report.Code);
        Assert.Equal(0, environment.Count("submit.Reviewer"));
        Assert.Contains("WorkspaceIndex", report.SourceDifferences);
    }

    [Fact]
    public async Task A_source_that_changes_during_the_review_is_not_qualified()
    {
        var environment = new ScriptedEnvironment();
        environment.SourceAt = _ => environment.Count("submit.Reviewer") > 0 ? QualificationReadings.Snapshot("edited") : QualificationReadings.Snapshot();

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal("SourceChanged", report.Code);
        Assert.Null(report.Verdict);
    }

    [Fact]
    public async Task Only_the_workspace_branch_the_host_was_asked_to_create_may_appear_during_setup()
    {
        var added = $"refs/heads/main {new string('a', 40)}\n{ScriptedEnvironment.WorkspaceReference} {new string('b', 40)}\n";
        var environment = new ScriptedEnvironment();
        environment.SourceAt = call => call == 1
            ? QualificationReadings.Snapshot()
            : QualificationReadings.Snapshot() with { References = added };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Qualified, report.Result);

        var foreign = new ScriptedEnvironment();
        foreign.SourceAt = call => call == 1
            ? QualificationReadings.Snapshot()
            : QualificationReadings.Snapshot() with { References = added.Replace("workspace", "someone-elses") };
        var otherLedger = new InvocationLedger(Path.Combine(LedgerDirectory, "second"), TimeProvider.System);
        var blocked = await new QualificationSession(otherLedger, foreign, Tiny, "another-session").RunAsync(CancellationToken.None);

        Assert.Equal(QualificationResult.Blocked, blocked.Result);
        Assert.Equal("SourceChangedDuringSetup", blocked.Code);
        Assert.Equal(0, foreign.Count("submit.Planner"));
    }

    [Theory]
    [InlineData("reply-to-another-message")]
    [InlineData("input-is-not-the-proposal")]
    [InlineData("review-targets-another-proposal")]
    [InlineData("sealed-input-names-another-proposal")]
    [InlineData("reply-is-from-the-wrong-provider")]
    [InlineData("reply-type-does-not-match-outcome")]
    [InlineData("second-extra-input")]
    public async Task A_foreign_or_misbound_review_is_not_qualified_and_triggers_nothing_further(string defect)
    {
        var environment = new ScriptedEnvironment();
        var other = Guid.NewGuid();
        environment.OnRead = (role, _) =>
        {
            if (role == AllowanceRole.Planner)
            {
                return QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId);
            }

            var good = QualificationReadings.Reviewer(environment.ReviewerAttemptId, environment.ReplyMessageId, environment.ProposalMessageId);
            return defect switch
            {
                "reply-to-another-message" => good with { InReplyToMessageId = other },
                "input-is-not-the-proposal" => good with { InputMessageIds = [other] },
                "review-targets-another-proposal" => good with { ReviewedProposalMessageId = other },
                "sealed-input-names-another-proposal" => good with { ManifestProposalMessageId = other },
                "reply-is-from-the-wrong-provider" => good with { Provider = "Codex" },
                "reply-type-does-not-match-outcome" => good with { MessageType = "Acceptance" },
                _ => good with { InputMessageIds = [environment.ProposalMessageId, other] },
            };
        };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.StartsWith("Reviewer", report.Code, StringComparison.Ordinal);
        Assert.Null(report.Verdict);
        Assert.Equal(1, environment.Count("submit.Planner"));
        Assert.Equal(1, environment.Count("submit.Reviewer"));
    }

    [Fact]
    public async Task An_ambiguous_review_submission_is_never_repeated_and_keeps_the_valid_proposal_fact()
    {
        var environment = new ScriptedEnvironment();
        environment.OnSubmit = (role, _) => role == AllowanceRole.Reviewer
            ? throw new HttpRequestException()
            : Task.FromResult(SubmissionResult.Accepted(environment.PlannerAttemptId));

        var report = await RunAsync(environment);

        Assert.Equal("ReviewerSubmissionAmbiguous", report.Code);
        Assert.Equal(1, environment.Count("submit.Reviewer"));
        Assert.Equal("Proposal", report.Planner!.MessageType);
        Assert.True(Ledger.IsConsumed(AllowanceRole.Reviewer));
    }

    [Fact]
    public async Task A_restart_cannot_reset_an_allowance_that_an_interrupted_session_consumed()
    {
        var first = new ScriptedEnvironment { OnSubmit = (_, _) => throw new TimeoutException() };
        await RunAsync(first, "first-session");

        var second = new ScriptedEnvironment();
        var restarted = await new QualificationSession(new InvocationLedger(LedgerDirectory, TimeProvider.System), second, Tiny, "second-session")
            .RunAsync(CancellationToken.None);

        Assert.Equal(QualificationResult.SessionSpent, restarted.Result);
        Assert.Empty(second.Calls);
    }

    [Fact]
    public async Task Concurrent_launches_cannot_both_submit_the_planner()
    {
        var gate = new TaskCompletionSource();
        var environments = new[] { new ScriptedEnvironment(), new ScriptedEnvironment() };
        foreach (var environment in environments)
        {
            environment.OnSubmit = async (role, _) =>
            {
                await gate.Task;
                return SubmissionResult.Accepted(Guid.NewGuid());
            };
        }

        var sessions = environments.Select((environment, index) => RunAsync(environment, $"launch-{index}")).ToArray();
        gate.SetResult();
        var reports = await Task.WhenAll(sessions);

        Assert.Equal(1, environments.Sum(environment => environment.Count("submit.Planner")));
        Assert.Contains(reports, report => report.Result == QualificationResult.SessionSpent);
    }

    [Fact]
    public async Task Cancelling_during_the_wait_submits_nothing_more()
    {
        using var cancellation = new CancellationTokenSource();
        var environment = new ScriptedEnvironment();
        environment.OnRead = (_, _) =>
        {
            cancellation.Cancel();
            return QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId) with { Status = "Running" };
        };

        var report = await RunAsync(environment, cancellationToken: cancellation.Token);

        Assert.Equal("Cancelled", report.Code);
        Assert.Equal(0, environment.Count("submit.Reviewer"));
        Assert.Equal(1, environment.Count("submit.Planner"));
        Assert.Equal(1, environment.Count("stop"));
    }

    [Fact]
    public async Task A_host_whose_shutdown_is_unproven_keeps_its_root_and_is_never_cleaned()
    {
        var environment = new ScriptedEnvironment { Shutdown = new ShutdownReport(true, false) };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Qualified, report.Result);
        Assert.Equal(0, environment.Count("cleanup"));
        Assert.Equal("ShutdownUnproven", report.Cleanup!.Reason);
        Assert.Equal(6, report.ExitCode);
    }

    [Fact]
    public async Task A_cleanup_that_fails_is_reported_as_a_preserved_root()
    {
        var environment = new ScriptedEnvironment { Cleanup = () => throw new IOException() };

        var report = await RunAsync(environment);

        Assert.Equal("CleanupFailed", report.Cleanup!.Reason);
        Assert.False(report.Cleanup.Removed);
    }

    [Fact]
    public async Task A_setup_failure_still_stops_the_host_and_removes_only_the_unspent_root()
    {
        var environment = new ScriptedEnvironment();
        environment.Targets = null!;

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Blocked, report.Result);
        Assert.False(Ledger.IsSpent());
        Assert.Equal(1, environment.Count("stop"));
        Assert.Equal(1, environment.Count("cleanup"));
    }
}
