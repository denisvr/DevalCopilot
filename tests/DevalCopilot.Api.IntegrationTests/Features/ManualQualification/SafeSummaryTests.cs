using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

public sealed class SafeSummaryTests : QualificationTestBase
{
    private static readonly string[] NoForbiddenValues = [];

    [Fact]
    public async Task A_qualified_session_summary_carries_the_allowlisted_facts_and_no_path_or_credential()
    {
        var environment = new ScriptedEnvironment
        {
            Targets = new TargetJudgement(true,
            [
                new TargetReport("codex", true, "Real", "NodeScript", "0.43.0"),
                new TargetReport("claude", true, "Real", "DirectExecutable", "2.1.0"),
            ]),
        };
        var report = await RunAsync(environment);

        var text = SafeSummary.Render(report, NoForbiddenValues);

        using var summary = JsonDocument.Parse(text);
        var root = summary.RootElement;
        Assert.Equal("Qualified", root.GetProperty("result").GetString());
        Assert.Equal("Challenge", root.GetProperty("verdict").GetString());
        Assert.Equal("0.43.0", root.GetProperty("targets")[0].GetProperty("observedVersion").GetString());
        Assert.True(root.GetProperty("allowances").GetProperty("planner").GetProperty("consumedBeforePost").GetBoolean());
        Assert.Equal(0, root.GetProperty("allowances").GetProperty("unused").GetArrayLength());
        Assert.True(root.GetProperty("reviewer").GetProperty("lineage").GetProperty("repliesToThePersistedProposal").GetBoolean());
        Assert.True(root.GetProperty("reviewer").GetProperty("lineage").GetProperty("inputIsExactlyThatProposal").GetBoolean());
        Assert.True(root.GetProperty("source").GetProperty("unchanged").GetBoolean());
        Assert.True(root.GetProperty("cleanup").GetProperty("ownedRootRemoved").GetBoolean());
        Assert.DoesNotMatch("[A-Za-z]:[/\\\\]", text);
        Assert.DoesNotContain("Bearer", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_blocked_session_lists_both_allowances_as_unused()
    {
        var environment = new ScriptedEnvironment
        {
            Targets = new TargetJudgement(false, [new TargetReport("codex", false, "NotObservedExecutableNotFound", null, null)]),
        };
        var report = await RunAsync(environment);

        using var summary = JsonDocument.Parse(SafeSummary.Render(report, NoForbiddenValues));
        var allowances = summary.RootElement.GetProperty("allowances");

        Assert.Equal(["planner", "reviewer"], allowances.GetProperty("unused").EnumerateArray().Select(item => item.GetString()));
        Assert.False(allowances.GetProperty("planner").GetProperty("postAttempted").GetBoolean());
        Assert.Equal("Blocked", summary.RootElement.GetProperty("result").GetString());
        Assert.Equal(JsonValueKind.Null, summary.RootElement.GetProperty("planner").ValueKind);
    }

    [Fact]
    public async Task A_failure_reports_actual_observed_invocations_apart_from_the_spent_allowances()
    {
        var environment = new ScriptedEnvironment { OnSubmit = (_, _) => throw new TimeoutException() };
        var report = await RunAsync(environment);

        using var summary = JsonDocument.Parse(SafeSummary.Render(report, NoForbiddenValues));
        var planner = summary.RootElement.GetProperty("allowances").GetProperty("planner");

        Assert.True(planner.GetProperty("consumedBeforePost").GetBoolean());
        Assert.True(planner.GetProperty("postAttempted").GetBoolean());
        Assert.False(planner.GetProperty("executionObserved").GetBoolean());
        Assert.Equal("Unknown", planner.GetProperty("invocationState").GetString());
        Assert.Equal("PlannerSubmissionAmbiguous", summary.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_forbidden_value_anywhere_replaces_the_whole_summary_with_a_fixed_verdict()
    {
        var environment = new ScriptedEnvironment
        {
            OnSubmit = (_, _) => Task.FromResult(SubmissionResult.Refused("hunter2")),
        };
        var report = await RunAsync(environment);

        var text = SafeSummary.Render(report, ["hunter2"]);

        Assert.Equal(RefusedSummary, text);
    }

    private const string RefusedSummary = "{\"schemaVersion\":4,\"result\":\"Failed\",\"code\":\"SummaryRefused\"}";

    [Fact]
    public async Task Schema_four_lists_each_stage_message_with_its_comparisons_beside_the_counts()
    {
        var report = await RunAsync(new ScriptedEnvironment());

        using var summary = JsonDocument.Parse(SafeSummary.Render(report, NoForbiddenValues));
        var root = summary.RootElement;

        Assert.Equal(4, SafeSummary.SchemaVersion);
        Assert.Equal(4, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Qualified", root.GetProperty("result").GetString());
        foreach (var (name, attempts) in new[] { ("planner", 1), ("reviewer", 2) })
        {
            var stage = root.GetProperty(name);
            Assert.Equal(attempts, stage.GetProperty("attemptsInRun").GetInt32());
            Assert.Equal(attempts, stage.GetProperty("dispatchedAttemptsInRun").GetInt32());
            Assert.Equal(1, stage.GetProperty("messageCount").GetInt32());
            Assert.Equal("Agrees", stage.GetProperty("evidenceRouteComparison").GetString());
            Assert.True(stage.GetProperty("evidenceRouteAgrees").GetBoolean());
            Assert.Equal("Agrees", stage.GetProperty("messageSetComparison").GetString());
            Assert.False(stage.GetProperty("messagesTruncated").GetBoolean());
            var message = Assert.Single(stage.GetProperty("messages").EnumerateArray());
            Assert.True(message.GetProperty("belongsToAcceptedAttempt").GetBoolean());
            Assert.Equal("Agrees", message.GetProperty("evidenceRouteComparison").GetString());
            Assert.Equal("Agrees", message.GetProperty("persistedMetadataComparison").GetString());
            Assert.Equal("ProviderObserved", message.GetProperty("provenance").GetString());
        }
    }

    [Fact]
    public async Task A_failed_reviewer_without_a_reply_is_reported_as_zero_messages_and_no_comparison_not_as_a_disagreement()
    {
        var environment = new ScriptedEnvironment();
        environment.OnRead = (role, _) => role == AllowanceRole.Planner
            ? QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            : QualificationReadings.Reviewer(environment.ReviewerAttemptId, environment.ReplyMessageId, environment.ProposalMessageId)
                with
            {
                Status = "Failed",
                Outcome = "ProviderInvocationFailed",
                ExitCode = 1,
                DurationMilliseconds = 129,
                MessageCount = 0,
                Messages = [],
                MessageSetComparison = EvidenceRouteComparisonStatus.Agrees,
            };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal("ReviewerAttemptFailedProviderInvocationFailed", report.Code);
        Assert.Equal(1, environment.Count("submit.Reviewer"));
        using var summary = JsonDocument.Parse(SafeSummary.Render(report, NoForbiddenValues));
        var reviewer = summary.RootElement.GetProperty("reviewer");
        Assert.Equal(2, reviewer.GetProperty("attemptsInRun").GetInt32());
        Assert.Equal(2, reviewer.GetProperty("dispatchedAttemptsInRun").GetInt32());
        Assert.Equal(0, reviewer.GetProperty("messageCount").GetInt32());
        Assert.Equal("NotAttempted", reviewer.GetProperty("evidenceRouteComparison").GetString());
        Assert.False(reviewer.GetProperty("evidenceRouteAgrees").GetBoolean());
        Assert.Equal(1, reviewer.GetProperty("process").GetProperty("exitCode").GetInt32());
        Assert.Equal(JsonValueKind.Array, reviewer.GetProperty("messages").ValueKind);
        Assert.Equal(0, reviewer.GetProperty("messages").GetArrayLength());
        Assert.False(reviewer.GetProperty("messagesTruncated").GetBoolean());
        // The timeline was enumerated and empty, and no row is stored: the empty sets were compared and agree.
        Assert.Equal("Agrees", reviewer.GetProperty("messageSetComparison").GetString());
        Assert.Equal("Agrees", summary.RootElement.GetProperty("planner").GetProperty("evidenceRouteComparison").GetString());
        var allowance = summary.RootElement.GetProperty("allowances").GetProperty("reviewer");
        Assert.Equal("ExecutionObserved", allowance.GetProperty("invocationState").GetString());
    }

    [Fact]
    public async Task An_attempt_still_running_at_its_deadline_reports_an_unobserved_message_count_as_null()
    {
        var environment = new ScriptedEnvironment();
        environment.OnRead = (_, _) => QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            with
        {
            Status = "Running",
            Outcome = null,
            MessageCount = null,
            Messages = [],
            MessageSetComparison = EvidenceRouteComparisonStatus.NotAttempted,
        };

        var report = await RunAsync(environment);

        using var summary = JsonDocument.Parse(SafeSummary.Render(report, NoForbiddenValues));
        var planner = summary.RootElement.GetProperty("planner");
        Assert.Equal("PlannerDeadlineBeforeTerminal", report.Code);
        Assert.Equal(JsonValueKind.Null, planner.GetProperty("messageCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, planner.GetProperty("messages").ValueKind);
        Assert.Equal(JsonValueKind.Null, planner.GetProperty("messagesTruncated").ValueKind);
        Assert.Equal("NotAttempted", planner.GetProperty("evidenceRouteComparison").GetString());
        Assert.Equal(JsonValueKind.Null, summary.RootElement.GetProperty("reviewer").ValueKind);
    }

    [Theory]
    [InlineData(2, EvidenceRouteComparisonStatus.NotAttempted, false)]
    [InlineData(1, EvidenceRouteComparisonStatus.Disagrees, true)]
    public async Task Multiple_messages_and_a_disagreeing_comparison_are_reported_as_what_was_observed(
        int messageCount, EvidenceRouteComparisonStatus comparison, bool selected)
    {
        var environment = new ScriptedEnvironment();
        environment.OnRead = (_, _) =>
        {
            var proposal = QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId);
            var second = proposal.Messages[0] with { Id = Guid.NewGuid(), EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted };
            return selected
                ? proposal.EditEach(message => message with { EvidenceRouteComparison = comparison })
                : proposal with { MessageCount = messageCount, Messages = [.. proposal.Messages, second] };
        };

        var report = await RunAsync(environment);

        using var summary = JsonDocument.Parse(SafeSummary.Render(report, NoForbiddenValues));
        var planner = summary.RootElement.GetProperty("planner");
        Assert.Equal(selected ? "PlannerEvidenceRouteDisagrees" : "PlannerMessageNotRecorded", report.Code);
        Assert.Equal(messageCount, planner.GetProperty("messageCount").GetInt32());
        Assert.Equal(messageCount, planner.GetProperty("messages").GetArrayLength());
        Assert.Equal(comparison.ToString(), planner.GetProperty("evidenceRouteComparison").GetString());
        Assert.False(planner.GetProperty("evidenceRouteAgrees").GetBoolean());
    }

    public static TheoryData<string, Func<StageReading, StageReading>> UnobservableReadings => new()
    {
        {
            "undefined message evidence comparison",
            reading => reading.EditEach(message => message with { EvidenceRouteComparison = (EvidenceRouteComparisonStatus)7 })
        },
        {
            "undefined stored row comparison",
            reading => reading.EditEach(message => message with { PersistedMetadataComparison = (EvidenceRouteComparisonStatus)7 })
        },
        { "undefined set comparison", reading => reading with { MessageSetComparison = (EvidenceRouteComparisonStatus)7 } },
        { "negative attempts", reading => reading with { AttemptsInRun = -1 } },
        { "negative dispatched attempts", reading => reading with { DispatchedAttemptsInRun = -1 } },
        { "negative message count", reading => reading with { MessageCount = -1 } },
        { "messages listed beside a zero count", reading => reading with { MessageCount = 0 } },
        { "two counted but one listed", reading => reading with { MessageCount = 2 } },
        { "messages listed without an enumerated count", reading => reading with { MessageCount = null } },
        { "a count without its list", reading => reading with { Messages = [] } },
        { "a message without an identity", reading => reading.EditEach(message => message with { Id = Guid.Empty }) },
        {
            "an enumerated count whose set comparison was never made",
            reading => reading with { MessageSetComparison = EvidenceRouteComparisonStatus.NotAttempted }
        },
        {
            "an observed zero whose set comparison was never made",
            reading => reading.WithoutMessages() with { MessageSetComparison = EvidenceRouteComparisonStatus.NotAttempted }
        },
        {
            "a set comparison performed without an enumerated count",
            reading => reading.WithUnenumeratedMessages() with { MessageSetComparison = EvidenceRouteComparisonStatus.Agrees }
        },
        {
            "evidence requested for an outcome that expects none",
            reading => reading with { Outcome = "ProviderInvocationFailed" }
        },
        {
            "evidence requested beyond the bound of the role",
            reading => reading with
            {
                MessageCount = 6,
                Messages = [.. Enumerable.Range(0, 6).Select(_ => reading.Messages[0] with { Id = Guid.NewGuid() })],
            }
        },
    };

    [Theory]
    [MemberData(nameof(UnobservableReadings))]
    public async Task A_stage_state_the_reader_cannot_produce_refuses_the_whole_summary(
        string defect, Func<StageReading, StageReading> corrupt)
    {
        Assert.False(string.IsNullOrEmpty(defect));
        var report = await RunAsync(new ScriptedEnvironment());
        var coherent = SafeSummary.Render(report, NoForbiddenValues);
        Assert.NotEqual(RefusedSummary, coherent);

        var planner = report.Planner!;
        var reviewer = report.Reviewer!;
        report.Planner = corrupt(planner);
        var plannerOnly = SafeSummary.Render(report, NoForbiddenValues);
        report.Planner = planner;
        report.Reviewer = corrupt(reviewer);
        var reviewerOnly = SafeSummary.Render(report, NoForbiddenValues);

        Assert.Equal(RefusedSummary, plannerOnly);
        Assert.Equal(RefusedSummary, reviewerOnly);
    }

    [Fact]
    public async Task An_unavailable_stage_reading_stays_null_whatever_it_holds()
    {
        var report = await RunAsync(new ScriptedEnvironment());
        report.Reviewer = new StageReading
        {
            AttemptsInRun = -1,
            MessageCount = -1,
            MessageSetComparison = (EvidenceRouteComparisonStatus)9,
        };

        using var summary = JsonDocument.Parse(SafeSummary.Render(report, NoForbiddenValues));

        Assert.Equal(JsonValueKind.Null, summary.RootElement.GetProperty("reviewer").ValueKind);
    }

    [Theory]
    [InlineData("C:/Users/someone/secret-project")]
    [InlineData("a b")]
    [InlineData("line\nbreak")]
    [InlineData("a\"quote")]
    public async Task A_free_text_code_never_reaches_the_summary(string hostile)
    {
        var environment = new ScriptedEnvironment
        {
            OnSubmit = (_, _) => Task.FromResult(SubmissionResult.Refused(hostile)),
        };
        var report = await RunAsync(environment);

        var text = SafeSummary.Render(report, NoForbiddenValues);

        Assert.DoesNotContain("someone", text, StringComparison.Ordinal);
        Assert.DoesNotContain("break", text, StringComparison.Ordinal);
        Assert.DoesNotContain("quote", text, StringComparison.Ordinal);
        using var _ = JsonDocument.Parse(text);
    }
}
