using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>A stage may qualify only from the attempt its own POST was accepted for: a coherent reading of some other attempt is
/// foreign evidence, however well it agrees with itself.</summary>
public sealed class AcceptedAttemptIdentityTests : QualificationTestBase
{
    [Fact]
    public async Task A_planner_observation_of_another_attempt_authorizes_neither_the_reviewer_nor_qualification()
    {
        var environment = new ScriptedEnvironment
        {
            OnSubmit = (_, _) => Task.FromResult(SubmissionResult.Accepted(Guid.NewGuid())),
        };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal("PlannerAttemptIdentityMismatch", report.Code);
        Assert.Null(report.Verdict);
        Assert.Equal(1, environment.Count("submit.Planner"));
        Assert.Equal(0, environment.Count("submit.Reviewer"));
        Assert.False(Ledger.IsConsumed(AllowanceRole.Reviewer));
    }

    [Fact]
    public async Task A_reviewer_observation_of_another_attempt_is_not_a_qualified_review()
    {
        var environment = new ScriptedEnvironment();
        environment.OnSubmit = (role, _) => Task.FromResult(SubmissionResult.Accepted(
            role == AllowanceRole.Planner ? environment.PlannerAttemptId : Guid.NewGuid()));

        var report = await RunAsync(environment);

        Assert.Equal("ReviewerAttemptIdentityMismatch", report.Code);
        Assert.Null(report.Verdict);
        Assert.Equal(1, environment.Count("submit.Planner"));
        Assert.Equal(1, environment.Count("submit.Reviewer"));
    }

    [Fact]
    public async Task A_foreign_attempt_that_is_still_running_is_a_mismatch_at_once_not_a_wait_for_a_deadline()
    {
        var environment = new ScriptedEnvironment
        {
            OnSubmit = (_, _) => Task.FromResult(SubmissionResult.Accepted(Guid.NewGuid())),
        };
        environment.OnRead = (_, _) => QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            with { Status = "Running", Outcome = null, MessageCount = 0, MessageId = null, MessageType = null };

        var report = await RunAsync(environment);

        Assert.Equal("PlannerAttemptIdentityMismatch", report.Code);
        Assert.Equal(1, environment.Count("read.Planner"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_accepted_answer_without_a_usable_attempt_identity_is_ambiguous_and_never_repeated(bool empty)
    {
        var environment = new ScriptedEnvironment
        {
            OnSubmit = (_, _) => Task.FromResult(new SubmissionResult(
                SubmissionOutcome.Accepted, empty ? Guid.Empty : null, null)),
        };

        var report = await RunAsync(environment);

        Assert.Equal("PlannerAcceptedAttemptMissing", report.Code);
        Assert.Equal(1, environment.Count("submit.Planner"));
        Assert.Equal(0, environment.Count("submit.Reviewer"));
        Assert.True(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(Ledger.IsConsumed(AllowanceRole.Reviewer));
    }

    [Fact]
    public async Task Matching_identities_qualify_and_the_summary_states_both_truthfully()
    {
        var environment = new ScriptedEnvironment();

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Qualified, report.Result);
        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        var planner = summary.RootElement.GetProperty("planner");
        Assert.Equal(environment.PlannerAttemptId, planner.GetProperty("acceptedAttemptId").GetGuid());
        Assert.Equal(environment.PlannerAttemptId, planner.GetProperty("attemptId").GetGuid());
        Assert.True(planner.GetProperty("attemptIdMatchesAccepted").GetBoolean());
    }

    [Fact]
    public async Task A_mismatch_is_reported_with_both_identities_and_no_match_claim()
    {
        var accepted = Guid.NewGuid();
        var environment = new ScriptedEnvironment
        {
            OnSubmit = (_, _) => Task.FromResult(SubmissionResult.Accepted(accepted)),
        };

        var report = await RunAsync(environment);

        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        var planner = summary.RootElement.GetProperty("planner");
        Assert.Equal(accepted, planner.GetProperty("acceptedAttemptId").GetGuid());
        Assert.Equal(environment.PlannerAttemptId, planner.GetProperty("attemptId").GetGuid());
        Assert.False(planner.GetProperty("attemptIdMatchesAccepted").GetBoolean());
        var allowance = summary.RootElement.GetProperty("allowances").GetProperty("planner");
        Assert.Equal("Unknown", allowance.GetProperty("invocationState").GetString());
        Assert.False(allowance.GetProperty("executionObserved").GetBoolean());
    }
}
