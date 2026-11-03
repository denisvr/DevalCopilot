using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>The ADR-0072 facts the qualification now requires of each stage: the recorded adapter contract version, the required I/O
/// completion and the closed invocation diagnostic, including for a failed attempt that produced no message.</summary>
public sealed class StageEvidenceFactsTests : QualificationTestBase
{
    private static readonly Guid PlannerAttempt = Guid.NewGuid();
    private static readonly Guid ReviewerAttempt = Guid.NewGuid();
    private static readonly Guid Proposal = Guid.NewGuid();
    private static readonly Guid Reply = Guid.NewGuid();

    private static StageReading Planner() => QualificationReadings.Planner(PlannerAttempt, Proposal);

    private static StageReading Reviewer() => QualificationReadings.Reviewer(ReviewerAttempt, Reply, Proposal);

    [Fact]
    public void The_pinned_contract_versions_are_the_production_ones()
    {
        Assert.Equal("codex-planning-v1", QualificationContracts.PlannerAdapter);
        Assert.Equal(
            DevalCopilot.Domain.Features.Runs.ReadOnlyFormatRepairPolicy.CriticalReviewAdapterContractVersion,
            QualificationContracts.ReviewerAdapter);
    }

    [Fact]
    public void Coherent_readings_with_the_current_contracts_and_complete_io_pass()
    {
        Assert.Null(StageVerifier.Verify(AllowanceRole.Planner, Planner(), null, PlannerAttempt));
        Assert.Null(StageVerifier.Verify(AllowanceRole.Reviewer, Reviewer(), Proposal, ReviewerAttempt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("claude-critical-review-v1")]
    [InlineData("claude-critical-review-v3")]
    [InlineData("codex-planning-v1")]
    public void A_missing_historical_or_foreign_reviewer_contract_never_qualifies(string version)
    {
        var reading = Reviewer() with { AdapterContractVersion = version };

        Assert.Equal("AdapterContractNotCurrent", StageVerifier.Verify(AllowanceRole.Reviewer, reading, Proposal, ReviewerAttempt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("codex-planning-v2")]
    [InlineData("claude-critical-review-v2")]
    public void A_missing_or_foreign_planner_contract_never_qualifies(string version)
    {
        var reading = Planner() with { AdapterContractVersion = version };

        Assert.Equal("AdapterContractNotCurrent", StageVerifier.Verify(AllowanceRole.Planner, reading, null, PlannerAttempt));
    }

    public static TheoryData<string, Func<StageReading, StageReading>> IncompleteIo => new()
    {
        { "stdout deadline", reading => reading with { StandardOutputCompletion = "DeadlineExceeded" } },
        { "stdout faulted", reading => reading with { StandardOutputCompletion = "Faulted" } },
        { "stdout unknown", reading => reading with { StandardOutputCompletion = "Unknown" } },
        { "stderr deadline", reading => reading with { StandardErrorCompletion = "DeadlineExceeded" } },
        { "stderr unknown", reading => reading with { StandardErrorCompletion = "Unknown" } },
        { "stdin deadline", reading => reading with { StandardInputDelivery = "DeadlineExceeded" } },
        { "stdin faulted", reading => reading with { StandardInputDelivery = "Faulted" } },
        { "stdin unknown", reading => reading with { StandardInputDelivery = "Unknown" } },
        { "stdout not recorded", reading => reading with { StandardOutputCompletion = null } },
        { "stderr not recorded", reading => reading with { StandardErrorCompletion = null } },
        { "stdin not recorded", reading => reading with { StandardInputDelivery = null } },
    };

    [Theory]
    [MemberData(nameof(IncompleteIo))]
    public void A_clean_zero_exit_with_incomplete_or_unrecorded_required_io_never_qualifies(
        string defect, Func<StageReading, StageReading> corrupt)
    {
        Assert.False(string.IsNullOrEmpty(defect));

        Assert.Equal("RequiredIoIncomplete", StageVerifier.Verify(AllowanceRole.Planner, corrupt(Planner()), null, PlannerAttempt));
        Assert.Equal("RequiredIoIncomplete", StageVerifier.Verify(AllowanceRole.Reviewer, corrupt(Reviewer()), Proposal, ReviewerAttempt));
    }

    [Fact]
    public void An_input_that_was_never_redirected_is_complete_only_beside_complete_outputs()
    {
        Assert.True((Planner() with { StandardInputDelivery = "NotRedirected" }).RequiredIoComplete);
        Assert.False((Planner() with { StandardInputDelivery = "NotRedirected", StandardErrorCompletion = "Faulted" }).RequiredIoComplete);
    }

    [Fact]
    public void A_disagreeing_or_unread_attempt_evidence_route_never_qualifies()
    {
        foreach (var comparison in new[] { EvidenceRouteComparisonStatus.Disagrees, EvidenceRouteComparisonStatus.NotAttempted })
        {
            var reading = Reviewer() with { AttemptEvidenceRouteComparison = comparison };

            Assert.Equal("AttemptEvidenceRouteDisagrees", StageVerifier.Verify(AllowanceRole.Reviewer, reading, Proposal, ReviewerAttempt));
        }
    }

    [Theory]
    [InlineData("Recorded")]
    [InlineData("Unknown")]
    public void A_recorded_invocation_diagnostic_beside_a_success_is_incoherent(string state)
    {
        var reading = Reviewer() with { InvocationDiagnosticState = state, InvocationDiagnosticCategory = "NonZeroExit" };

        Assert.Equal("UnexpectedInvocationDiagnostic", StageVerifier.Verify(AllowanceRole.Reviewer, reading, Proposal, ReviewerAttempt));
    }

    [Fact]
    public async Task A_failed_claude_attempt_with_no_message_reports_its_contract_io_and_closed_diagnostic_and_is_a_valid_failure_report()
    {
        var environment = new ScriptedEnvironment();
        environment.OnRead = (role, _) => role == AllowanceRole.Planner
            ? QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            : QualificationReadings.Reviewer(environment.ReviewerAttemptId, environment.ReplyMessageId, environment.ProposalMessageId) with
            {
                Status = "Failed",
                Outcome = "ProviderInvocationFailed",
                ExitCode = 1,
                StandardOutputCompletion = "Complete",
                StandardErrorCompletion = "Complete",
                StandardInputDelivery = "Complete",
                InvocationDiagnosticState = "Recorded",
                InvocationDiagnosticCategory = "AuthenticationRequired",
                InvocationDiagnosticVersion = "agent-invocation-diagnostic-v1",
                MessageCount = 0,
                MessageId = null,
                MessageType = null,
                InReplyToMessageId = null,
                MessageAttemptId = null,
                MessageActor = null,
                MessageProvenance = null,
                EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted,
                AttemptEvidenceRouteComparison = EvidenceRouteComparisonStatus.Agrees,
            };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal("ReviewerAttemptFailedProviderInvocationFailed", report.Code);
        Assert.Equal(1, environment.Count("submit.Reviewer"));
        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        var reviewer = summary.RootElement.GetProperty("reviewer");
        Assert.Equal("claude-critical-review-v2", reviewer.GetProperty("adapterContractVersion").GetString());
        Assert.True(reviewer.GetProperty("ioCompletion").GetProperty("requiredComplete").GetBoolean());
        Assert.Equal("Complete", reviewer.GetProperty("ioCompletion").GetProperty("standardOutput").GetString());
        var diagnostic = reviewer.GetProperty("invocationDiagnostic");
        Assert.Equal("Recorded", diagnostic.GetProperty("state").GetString());
        Assert.Equal("AuthenticationRequired", diagnostic.GetProperty("category").GetString());
        Assert.Equal("agent-invocation-diagnostic-v1", diagnostic.GetProperty("version").GetString());
        Assert.True(reviewer.GetProperty("attemptEvidenceRouteAgrees").GetBoolean());
        Assert.Equal(0, reviewer.GetProperty("messageCount").GetInt32());
    }

    [Fact]
    public async Task A_generic_or_unknown_diagnosis_is_reported_as_it_is_and_never_relabelled()
    {
        var environment = new ScriptedEnvironment();
        environment.OnRead = (role, _) => role == AllowanceRole.Planner
            ? QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            : QualificationReadings.Reviewer(environment.ReviewerAttemptId, environment.ReplyMessageId, environment.ProposalMessageId) with
            {
                Status = "Failed",
                Outcome = "ProviderInvocationFailed",
                ExitCode = 1,
                InvocationDiagnosticState = "Unknown",
                MessageCount = 0,
                MessageId = null,
                MessageType = null,
                InReplyToMessageId = null,
                MessageAttemptId = null,
                MessageActor = null,
                MessageProvenance = null,
                EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted,
            };

        var report = await RunAsync(environment);

        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        var diagnostic = summary.RootElement.GetProperty("reviewer").GetProperty("invocationDiagnostic");
        Assert.Equal("Unknown", diagnostic.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, diagnostic.GetProperty("category").ValueKind);
        Assert.Equal(1, environment.Count("submit.Reviewer"));
    }

    [Fact]
    public async Task A_failed_attempt_with_incomplete_io_reports_the_unfinished_streams_without_any_output()
    {
        var environment = new ScriptedEnvironment();
        environment.OnRead = (role, _) => role == AllowanceRole.Planner
            ? QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            : QualificationReadings.Reviewer(environment.ReviewerAttemptId, environment.ReplyMessageId, environment.ProposalMessageId) with
            {
                Status = "Failed",
                Outcome = "ProviderInvocationFailed",
                ExitCode = 0,
                StandardOutputCompletion = "DeadlineExceeded",
                StandardErrorCompletion = "Complete",
                StandardInputDelivery = "Complete",
                InvocationDiagnosticState = "Recorded",
                InvocationDiagnosticCategory = "CaptureDeadlineExceeded",
                InvocationDiagnosticVersion = "agent-invocation-diagnostic-v1",
                MessageCount = 0,
                MessageId = null,
                MessageType = null,
                InReplyToMessageId = null,
                MessageAttemptId = null,
                MessageActor = null,
                MessageProvenance = null,
                EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted,
            };

        var report = await RunAsync(environment);

        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        var io = summary.RootElement.GetProperty("reviewer").GetProperty("ioCompletion");
        Assert.False(io.GetProperty("requiredComplete").GetBoolean());
        Assert.Equal("DeadlineExceeded", io.GetProperty("standardOutput").GetString());
    }

    [Fact]
    public async Task A_foreign_attempt_with_otherwise_perfect_facts_never_qualifies()
    {
        var environment = new ScriptedEnvironment();
        environment.OnRead = (role, _) => role == AllowanceRole.Planner
            ? QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            : QualificationReadings.Reviewer(environment.ReviewerAttemptId, environment.ReplyMessageId, environment.ProposalMessageId) with { AttemptId = Guid.NewGuid(), MessageAttemptId = null };

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal("ReviewerAttemptIdentityMismatch", report.Code);
    }

    [Fact]
    public async Task A_summary_with_a_refused_free_text_diagnostic_category_replaces_it_with_a_closed_token()
    {
        var environment = new ScriptedEnvironment();
        environment.OnRead = (role, _) => role == AllowanceRole.Planner
            ? QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            : QualificationReadings.Reviewer(environment.ReviewerAttemptId, environment.ReplyMessageId, environment.ProposalMessageId) with
            {
                Status = "Failed",
                Outcome = "ProviderInvocationFailed",
                InvocationDiagnosticState = "Recorded",
                InvocationDiagnosticCategory = "C:/Users/someone/token sk-secret",
                InvocationDiagnosticVersion = "agent-invocation-diagnostic-v1",
                MessageCount = 0,
                MessageId = null,
                MessageType = null,
                InReplyToMessageId = null,
                MessageAttemptId = null,
                MessageActor = null,
                MessageProvenance = null,
                EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted,
            };

        var report = await RunAsync(environment);
        var text = SafeSummary.Render(report, []);

        Assert.DoesNotContain("someone", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-secret", text, StringComparison.Ordinal);
        using var summary = JsonDocument.Parse(text);
        Assert.Equal("unrecognized", summary.RootElement.GetProperty("reviewer").GetProperty("invocationDiagnostic").GetProperty("category").GetString());
    }
}
