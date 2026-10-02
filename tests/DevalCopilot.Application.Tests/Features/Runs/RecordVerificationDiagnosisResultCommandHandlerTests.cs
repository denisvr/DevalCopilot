using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisResult;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Atomic recording of a verification-diagnosis result (ADR-0018): findings or one escalation replying to the diagnosed
/// report, never an approval and never a checkpoint review; a fresh untracked applicability check turns a stale semantic
/// response into a truthful VerificationEvidenceChanged while retaining the artifacts and process evidence; Git fingerprint
/// drift stays SourceChanged; every malformed request is refused without mutation.
/// </summary>
public sealed class RecordVerificationDiagnosisResultCommandHandlerTests : IAsyncLifetime
{
    private static readonly IReadOnlyList<SealedVerificationDiagnosisArtifact> NoArtifacts = [];

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static ValidatedReviewFinding Finding(int index) =>
        new("high", "correctness", $"Finding {index}", $"Evidence {index}", $"Change {index}", index % 2 == 0 ? "src/Foo.cs" : null);

    private static ValidatedVerificationDiagnosis Findings(int count) =>
        ValidatedVerificationDiagnosis.CreateFindings("Diagnosis summary.", Enumerable.Range(1, count).Select(Finding).ToArray());

    private static ValidatedVerificationDiagnosis Escalation() => ValidatedVerificationDiagnosis.CreateEscalation(
        "Needs a human decision.",
        new ValidatedDiagnosisEscalation("Decide the tool version", "Keep or change it", "It keeps failing", "Repeats on every run", "Change it"));

    private async Task<(DiagnosisTestScene Scene, Attempt Attempt)> DispatchedAsync(params Spec[] specs)
    {
        var scene = await CreateAsync(_fixture, specs.Length == 0 ? [Spec.Passed(), Spec.Failed()] : specs);
        var seeded = await scene.AddDiagnosisAsync(outcome: null, dispatched: true);
        return (scene, seeded.Attempt);
    }

    private Task<Devalente.Shared.Results.Result<RecordVerificationDiagnosisResultCommandResult>> RecordAsync(
        DiagnosisTestScene scene, Attempt attempt, AgentOutcome outcome, ValidatedVerificationDiagnosis? diagnosis,
        string? fingerprint = "default", IReadOnlyList<SealedVerificationDiagnosisArtifact>? artifacts = null,
        AgentProcessEvidence? processEvidence = null, string? providerSessionId = null, AgentTokenUsage? usage = null,
        Guid? runId = null, Guid? attemptId = null)
    {
        var db = _fixture.CreateContext();
        var handler = new RecordVerificationDiagnosisResultCommandHandler(db, new FixedTimeProvider(Now.AddMinutes(1)));
        return handler.HandleAsync(
            new RecordVerificationDiagnosisResultCommand(
                runId ?? scene.Run.Id, attemptId ?? attempt.Id, outcome, fingerprint == "default" ? scene.Implementation.ReviewFingerprint : fingerprint,
                artifacts ?? NoArtifacts, diagnosis, providerSessionId, processEvidence ?? TestProcessEvidence.ReportedCleanExit, usage),
            CancellationToken.None);
    }

    private static string Code<T>(Devalente.Shared.Results.Result<T> result) => Assert.Single(result.Errors).Code;

    private static SealedVerificationDiagnosisArtifact Artifact(ArtifactPurpose purpose) =>
        new(purpose, $"runs/x/{purpose}.sealed", 12, "sha256:abc", false);

    private async Task AssertUntouchedAsync(DiagnosisTestScene scene, Attempt attempt, (int Attempts, int Artifacts, int Inputs, int Evidence, int Messages, int Events) before)
    {
        Assert.Equal(before, await scene.CountsAsync());
        await using var verify = _fixture.CreateContext();
        var stored = await verify.Attempts.SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(AttemptStatus.Running, stored.Status);
        Assert.Null(stored.AgentOutcome);
        Assert.Null(stored.AgentProcessOutcome);
    }

    // ---- Findings --------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public async Task Findings_are_recorded_in_order_as_one_review_finding_each_replying_to_the_report_for_claude(int count)
    {
        var (scene, attempt) = await DispatchedAsync();

        var result = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(count));

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Equal(AttemptStatus.Completed, result.Value.Status);
        Assert.Equal(AgentOutcome.DiagnosisFindingsRecorded, result.Value.Outcome);
        await using var verify = _fixture.CreateContext();
        var stored = await verify.Attempts.SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(AttemptStatus.Completed, stored.Status);
        Assert.Equal(AgentOutcome.DiagnosisFindingsRecorded, stored.AgentOutcome);
        Assert.Equal(TestProcessEvidence.CleanExit, stored.GetAgentProcessExecutionEvidence());

        var messages = await verify.CollaborationMessages.Where(m => m.AttemptId == attempt.Id).OrderBy(m => m.Sequence).ToListAsync();
        Assert.Equal(count, messages.Count);
        Assert.Equal(Enumerable.Range(1, count).Select(i => $"Finding {i}"), messages.Select(m => m.Summary));
        Assert.All(messages, message =>
        {
            Assert.Equal(CollaborationMessageType.ReviewFinding, message.Type);
            Assert.Equal(scene.ReportId, message.InReplyToMessageId);
            Assert.Equal(ParticipantIdentity.ForAgent(AgentRole.CodeReviewer, AgentProvider.Codex), message.Actor);
            Assert.Equal(ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), message.Recipient);
            Assert.Equal(CollaborationMessageProvenance.ProviderObserved, message.Provenance);
            Assert.Equal(["severity", "category", "evidence", "requiredChange"], JsonDocument.Parse(message.StructuredContentJson).RootElement.EnumerateObject().Select(p => p.Name));
        });
        var events = await verify.Events.Where(e => e.AttemptId == attempt.Id).OrderBy(e => e.Sequence).ToListAsync();
        Assert.Equal(count, events.Count);
        Assert.All(events, e => Assert.Equal(RunEventType.CollaborationMessageRecorded, e.EventType));
        Assert.Equal(events[^1].Sequence, result.Value.LatestEventSequence);
    }

    [Fact]
    public async Task A_diagnosis_never_records_an_approval_or_a_checkpoint_review()
    {
        var (findingsScene, findingsAttempt) = await DispatchedAsync();
        Assert.True((await RecordAsync(findingsScene, findingsAttempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(2))).IsSuccess);
        var (escalationScene, escalationAttempt) = await DispatchedAsync();
        Assert.True((await RecordAsync(escalationScene, escalationAttempt, AgentOutcome.DiagnosisEscalated, Escalation())).IsSuccess);

        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.CheckpointReviews);
        Assert.Empty(verify.CheckpointReviewEvidence);
        Assert.Empty(verify.CollaborationMessages.Where(m => m.Type == CollaborationMessageType.ReviewApproval));
    }

    [Fact]
    public async Task An_escalation_is_recorded_as_exactly_one_escalation_replying_to_the_report_for_the_human()
    {
        var (scene, attempt) = await DispatchedAsync();

        var result = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisEscalated, Escalation());

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Equal(AgentOutcome.DiagnosisEscalated, result.Value.Outcome);
        await using var verify = _fixture.CreateContext();
        var message = Assert.Single(await verify.CollaborationMessages.Where(m => m.AttemptId == attempt.Id).ToListAsync());
        Assert.Equal(CollaborationMessageType.Escalation, message.Type);
        Assert.Equal(scene.ReportId, message.InReplyToMessageId);
        Assert.Equal(ParticipantIdentity.ForHuman(), message.Recipient);
        Assert.Equal(ParticipantIdentity.ForAgent(AgentRole.CodeReviewer, AgentProvider.Codex), message.Actor);
        Assert.Equal("Needs a human decision.", message.Summary);
        Assert.Equal(
            ["unresolvedDecision", "options", "consequences", "evidence", "recommendedChoice"],
            JsonDocument.Parse(message.StructuredContentJson).RootElement.EnumerateObject().Select(p => p.Name));
        var recorded = Assert.Single(await verify.Events.Where(e => e.AttemptId == attempt.Id).ToListAsync());
        Assert.Equal(result.Value.LatestEventSequence, recorded.Sequence);
        var stored = await verify.Attempts.SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(AgentOutcome.DiagnosisEscalated, stored.AgentOutcome);
        Assert.Equal(AttemptStatus.Completed, stored.Status);
    }

    [Fact]
    public async Task Artifact_metadata_provider_session_and_token_usage_are_recorded_atomically_with_the_result()
    {
        var (scene, attempt) = await DispatchedAsync();

        var result = await RecordAsync(
            scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(1),
            artifacts: [Artifact(ArtifactPurpose.AgentStandardOutput), Artifact(ArtifactPurpose.AgentStandardError), Artifact(ArtifactPurpose.AgentFinalResponse)],
            providerSessionId: "session-1", usage: TestTokenUsage.CodexReported);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await using var verify = _fixture.CreateContext();
        var artifacts = await verify.Artifacts.Where(a => a.AttemptId == attempt.Id).OrderBy(a => a.Purpose).ToListAsync();
        Assert.Equal(
            new[] { ArtifactPurpose.AgentStandardOutput, ArtifactPurpose.AgentStandardError, ArtifactPurpose.AgentFinalResponse }.Order(),
            artifacts.Select(a => a.Purpose).Order());
        Assert.All(artifacts, a => Assert.Equal(ArtifactSensitivity.RedactedBestEffort, a.Sensitivity));
        var stored = await verify.Attempts.SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal("session-1", stored.AgentProviderSessionId);
        Assert.Equal(TestTokenUsage.CodexEvidence, stored.GetAgentTokenUsageEvidence());
    }

    // ---- Non-semantic outcomes -------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_invalid_structured_output_fails_the_attempt_keeping_artifacts_and_process_evidence_with_no_messages()
    {
        var (scene, attempt) = await DispatchedAsync();

        var result = await RecordAsync(
            scene, attempt, AgentOutcome.InvalidStructuredOutput, diagnosis: null,
            artifacts: [Artifact(ArtifactPurpose.AgentFinalResponse)]);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Equal(AttemptStatus.Failed, result.Value.Status);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, result.Value.Outcome);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == attempt.Id));
        Assert.Single(verify.Artifacts.Where(a => a.AttemptId == attempt.Id));
        var stored = await verify.Attempts.SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(TestProcessEvidence.CleanExit, stored.GetAgentProcessExecutionEvidence());
        var recorded = Assert.Single(await verify.Events.Where(e => e.AttemptId == attempt.Id).ToListAsync());
        Assert.Equal(RunEventType.AgentAttemptCompleted, recorded.EventType);
    }

    [Theory]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.CheckpointEvidenceUnavailable)]
    public async Task A_provider_failure_records_the_truthful_non_clean_process_evidence_without_messages(AgentOutcome outcome)
    {
        var (scene, attempt) = await DispatchedAsync();
        var failedProcess = new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 2, TimeSpan.FromSeconds(3));

        var result = await RecordAsync(scene, attempt, outcome, null, fingerprint: null, processEvidence: failedProcess);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Equal(AttemptStatus.Failed, result.Value.Status);
        await using var verify = _fixture.CreateContext();
        var stored = await verify.Attempts.SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(ProcessOutcome.Exited, stored.AgentProcessOutcome);
        Assert.Equal(2, stored.AgentProcessExitCode);
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == attempt.Id));
    }

    // ---- Closed outcome policy and refused shapes ------------------------------------------------------------------------

    [Theory]
    [InlineData(AgentOutcome.ReviewApproved)]
    [InlineData(AgentOutcome.ReviewChangesRequested)]
    [InlineData(AgentOutcome.CorrectionApplied)]
    [InlineData(AgentOutcome.Implemented)]
    [InlineData(AgentOutcome.Proposed)]
    [InlineData(AgentOutcome.SourceChanged)]
    [InlineData(AgentOutcome.WorkspaceNoLongerEligible)]
    [InlineData(AgentOutcome.InputAlreadyDiagnosed)]
    [InlineData(AgentOutcome.VerificationEvidenceChanged)]
    public async Task An_outcome_outside_the_closed_caller_selectable_set_is_refused_without_mutation(AgentOutcome outcome)
    {
        var (scene, attempt) = await DispatchedAsync();
        var before = await scene.CountsAsync();

        var result = await RecordAsync(scene, attempt, outcome, null);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.outcome_not_caller_selectable", Code(result));
        await AssertUntouchedAsync(scene, attempt, before);
    }

    [Fact]
    public async Task An_undefined_outcome_is_refused_without_mutation()
    {
        var (scene, attempt) = await DispatchedAsync();
        var before = await scene.CountsAsync();

        var result = await RecordAsync(scene, attempt, (AgentOutcome)99, null);

        Assert.Equal("agent_attempts.invalid_outcome", Code(result));
        await AssertUntouchedAsync(scene, attempt, before);
    }

    [Fact]
    public async Task A_semantic_outcome_without_a_diagnosis_or_without_a_completion_fingerprint_is_refused()
    {
        var (scene, attempt) = await DispatchedAsync();
        var before = await scene.CountsAsync();

        var noDiagnosis = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, null);
        var noFingerprint = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(1), fingerprint: null);
        var blankFingerprint = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisEscalated, Escalation(), fingerprint: "  ");

        Assert.Equal("agent_attempts.diagnosis_requires_validated_diagnosis", Code(noDiagnosis));
        Assert.Equal("agent_attempts.diagnosis_requires_completion_fingerprint", Code(noFingerprint));
        Assert.Equal("agent_attempts.diagnosis_requires_completion_fingerprint", Code(blankFingerprint));
        await AssertUntouchedAsync(scene, attempt, before);
    }

    [Fact]
    public async Task A_diagnosis_shape_that_disagrees_with_the_outcome_is_refused()
    {
        var (scene, attempt) = await DispatchedAsync();
        var before = await scene.CountsAsync();

        var escalationForFindings = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Escalation());
        var findingsForEscalation = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisEscalated, Findings(2));

        Assert.Equal("agent_attempts.invalid_diagnosis_shape", Code(escalationForFindings));
        Assert.Equal("agent_attempts.invalid_diagnosis_shape", Code(findingsForEscalation));
        await AssertUntouchedAsync(scene, attempt, before);
    }

    [Fact]
    public async Task Zero_or_eleven_findings_are_refused()
    {
        var (scene, attempt) = await DispatchedAsync();
        var before = await scene.CountsAsync();

        var zero = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(0));
        var eleven = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(11));

        Assert.Equal("agent_attempts.invalid_diagnosis_shape", Code(zero));
        Assert.Equal("agent_attempts.invalid_diagnosis_shape", Code(eleven));
        await AssertUntouchedAsync(scene, attempt, before);
    }

    [Fact]
    public async Task A_diagnosis_supplied_for_a_non_semantic_outcome_is_refused()
    {
        var (scene, attempt) = await DispatchedAsync();
        var before = await scene.CountsAsync();

        var result = await RecordAsync(scene, attempt, AgentOutcome.InvalidStructuredOutput, Findings(1));

        Assert.Equal("agent_attempts.diagnosis_requires_diagnosis_outcome", Code(result));
        await AssertUntouchedAsync(scene, attempt, before);
    }

    [Fact]
    public async Task Content_that_fails_the_policy_at_the_handler_boundary_is_refused_even_when_the_parser_was_bypassed()
    {
        var (scene, attempt) = await DispatchedAsync();
        var before = await scene.CountsAsync();
        var unsafeFinding = ValidatedVerificationDiagnosis.CreateFindings(
            "Summary.", [new ValidatedReviewFinding("high", "correctness", "Finding", "The password is wrong", "Change", null)]);
        var unsafeSummary = ValidatedVerificationDiagnosis.CreateFindings("The secret is out", [Finding(1)]);

        var findingResult = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, unsafeFinding);
        var summaryResult = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, unsafeSummary);

        Assert.Equal("agent_attempts.invalid_diagnosis_content", Code(findingResult));
        Assert.Equal("agent_attempts.invalid_diagnosis_content", Code(summaryResult));
        await AssertUntouchedAsync(scene, attempt, before);
    }

    [Fact]
    public async Task A_semantic_outcome_without_clean_exit_process_evidence_is_refused_without_mutation()
    {
        var (scene, attempt) = await DispatchedAsync();
        var before = await scene.CountsAsync();

        var result = await RecordAsync(
            scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(1),
            processEvidence: new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 1, TimeSpan.Zero));

        Assert.True(result.IsFailure);
        await AssertUntouchedAsync(scene, attempt, before);
    }

    // ---- Artifacts -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unsupported_or_duplicate_artifact_purpose_is_refused_without_mutation()
    {
        var (scene, attempt) = await DispatchedAsync();
        var before = await scene.CountsAsync();

        var unsupported = await RecordAsync(
            scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(1), artifacts: [Artifact(ArtifactPurpose.AgentContextManifest)]);
        var duplicate = await RecordAsync(
            scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(1),
            artifacts: [Artifact(ArtifactPurpose.AgentStandardOutput), Artifact(ArtifactPurpose.AgentStandardOutput)]);

        Assert.Equal("agent_attempts.unsupported_artifact_purpose", Code(unsupported));
        Assert.Equal("agent_attempts.duplicate_artifact_purpose", Code(duplicate));
        await AssertUntouchedAsync(scene, attempt, before);
    }

    [Theory]
    [InlineData("", "sha256:abc", 1)]
    [InlineData("runs/x.sealed", "", 1)]
    [InlineData("runs/x.sealed", "sha256:abc", -1)]
    public async Task Malformed_artifact_metadata_is_refused_without_mutation(string path, string hash, long length)
    {
        var (scene, attempt) = await DispatchedAsync();
        var before = await scene.CountsAsync();

        var result = await RecordAsync(
            scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(1),
            artifacts: [new SealedVerificationDiagnosisArtifact(ArtifactPurpose.AgentFinalResponse, path, length, hash, false)]);

        Assert.Equal("agent_attempts.invalid_artifact_metadata", Code(result));
        await AssertUntouchedAsync(scene, attempt, before);
    }

    [Fact]
    public async Task An_overlong_provider_session_identifier_is_refused_without_mutation()
    {
        var (scene, attempt) = await DispatchedAsync();
        var before = await scene.CountsAsync();

        var result = await RecordAsync(
            scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(1), providerSessionId: new string('s', 257));

        Assert.Equal("agent_attempts.provider_session_id_too_long", Code(result));
        await AssertUntouchedAsync(scene, attempt, before);
    }

    // ---- Attempt identity ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_run_or_attempt_or_an_attempt_of_another_run_is_not_found()
    {
        var (scene, attempt) = await DispatchedAsync();

        Assert.Equal("runs.not_found", Code(await RecordAsync(scene, attempt, AgentOutcome.InvalidStructuredOutput, null, runId: Guid.NewGuid())));
        Assert.Equal("runs.not_found", Code(await RecordAsync(scene, attempt, AgentOutcome.InvalidStructuredOutput, null, attemptId: Guid.NewGuid())));
    }

    [Fact]
    public async Task An_ordinary_implementation_review_attempt_cannot_record_a_diagnosis_result()
    {
        var scene = await DiagnosisTestScene.CreateAsync(_fixture);
        var review = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), scene.Run.Id, scene.Scene.Lineage.ReserveAttemptNumber(), scene.Scene.Workspace.Id, scene.Implementation.ReviewCheckpoint.Id,
            scene.Implementation.ReviewFingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now,
            scene.Scene.Lineage.NextAttemptNumber - 1);
        review.MarkAgentDispatched(Now);
        scene.Db.Attempts.Add(review);
        await scene.SaveAsync();
        var before = await scene.CountsAsync();

        var result = await RecordAsync(scene, review, AgentOutcome.DiagnosisFindingsRecorded, Findings(1));

        Assert.Equal("attempts.not_verification_diagnosis", Code(result));
        Assert.Equal(before, await scene.CountsAsync());
    }

    [Fact]
    public async Task An_undispatched_attempt_cannot_record_a_provider_result()
    {
        var scene = await DiagnosisTestScene.CreateAsync(_fixture);
        var undispatched = (await scene.AddDiagnosisAsync(outcome: null, dispatched: false)).Attempt;
        var before = await scene.CountsAsync();

        var result = await RecordAsync(scene, undispatched, AgentOutcome.DiagnosisFindingsRecorded, Findings(1));

        Assert.Equal("agent_attempts.not_dispatched", Code(result));
        Assert.Equal(before, await scene.CountsAsync());
    }

    [Fact]
    public async Task A_second_result_for_an_already_concluded_attempt_is_refused_and_records_nothing_more()
    {
        var (scene, attempt) = await DispatchedAsync();
        Assert.True((await RecordAsync(scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(2))).IsSuccess);
        var before = await scene.CountsAsync();

        var second = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(3));

        Assert.Equal("attempts.not_active", Code(second));
        Assert.Equal(before, await scene.CountsAsync());
    }

    [Fact]
    public async Task A_diagnosis_attempt_without_its_single_report_input_is_refused()
    {
        var (scene, attempt) = await DispatchedAsync();
        await scene.SqlAsync("DELETE FROM attempt_input_messages WHERE AttemptId = {0}", attempt.Id);
        var before = await scene.CountsAsync();

        var result = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(1));

        Assert.Equal("agent_attempts.diagnosis_input_invalid", Code(result));
        Assert.Equal(before, await scene.CountsAsync());
    }

    // ---- Staleness and drift ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_findings_response_for_verification_evidence_that_changed_is_recorded_as_evidence_changed_with_nothing_semantic()
    {
        var (scene, attempt) = await DispatchedAsync();
        await scene.AddExecutionAsync(0, Spec.Failed("newer", "newer"));

        var result = await RecordAsync(
            scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(3),
            artifacts: [Artifact(ArtifactPurpose.AgentStandardOutput), Artifact(ArtifactPurpose.AgentFinalResponse)],
            providerSessionId: "session-stale", usage: TestTokenUsage.CodexReported);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Equal(AttemptStatus.Failed, result.Value.Status);
        Assert.Equal(AgentOutcome.VerificationEvidenceChanged, result.Value.Outcome);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == attempt.Id));
        Assert.Equal(2, verify.Artifacts.Count(a => a.AttemptId == attempt.Id));
        var stored = await verify.Attempts.SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(AgentOutcome.VerificationEvidenceChanged, stored.AgentOutcome);
        Assert.Equal(TestProcessEvidence.CleanExit, stored.GetAgentProcessExecutionEvidence());
        Assert.Equal("session-stale", stored.AgentProviderSessionId);
        Assert.Equal(TestTokenUsage.CodexEvidence, stored.GetAgentTokenUsageEvidence());
        var recorded = Assert.Single(await verify.Events.Where(e => e.AttemptId == attempt.Id).ToListAsync());
        Assert.Equal(RunEventType.AgentAttemptCompleted, recorded.EventType);
        Assert.Equal(result.Value.LatestEventSequence, recorded.Sequence);
    }

    [Fact]
    public async Task An_escalation_response_for_changed_evidence_is_equally_not_recorded()
    {
        var (scene, attempt) = await DispatchedAsync();
        await scene.AddExecutionAsync(1, Spec.Passed());

        var result = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisEscalated, Escalation());

        Assert.Equal(AgentOutcome.VerificationEvidenceChanged, result.Value.Outcome);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == attempt.Id));
    }

    [Theory]
    [InlineData("new-enabled-command")]
    [InlineData("disabled-command")]
    [InlineData("timed-out-latest")]
    [InlineData("report-re-pointed")]
    [InlineData("new-checkpoint")]
    [InlineData("lease-released")]
    [InlineData("run-interrupted")]
    public async Task Any_change_to_what_the_claim_pinned_turns_the_semantic_response_into_evidence_changed(string change)
    {
        var (scene, attempt) = await DispatchedAsync(Spec.Passed(), Spec.Failed());
        switch (change)
        {
            case "new-enabled-command":
                await scene.AddEnabledCommandAsync();
                break;
            case "disabled-command":
                await scene.SetEnabledAsync(0, enabled: false);
                break;
            case "timed-out-latest":
                await scene.AddExecutionAsync(1, new Spec(Kind.TimedOut));
                break;
            case "report-re-pointed":
                await scene.ReplyReportToAsync((await scene.AddUnrelatedRootAsync()).Id);
                await scene.SaveAsync();
                break;
            case "new-checkpoint":
                await scene.AddCheckpointAsync();
                break;
            case "lease-released":
                await scene.ReleaseLeaseAsync();
                break;
            default:
                await scene.SqlAsync("UPDATE runs SET Lifecycle = 'Interrupted' WHERE Id = {0}", scene.Run.Id);
                break;
        }

        var result = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(1));

        Assert.True(result.IsSuccess, change);
        Assert.Equal(AgentOutcome.VerificationEvidenceChanged, result.Value.Outcome);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task Git_fingerprint_drift_stays_source_changed_and_records_no_messages()
    {
        var (scene, attempt) = await DispatchedAsync();

        var result = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(2), fingerprint: new string('9', 64));

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.SourceChanged, result.Value.Outcome);
        Assert.Equal(AttemptStatus.Failed, result.Value.Status);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task Fingerprint_drift_wins_over_changed_verification_evidence_as_the_git_source_classification()
    {
        var (scene, attempt) = await DispatchedAsync();
        await scene.AddExecutionAsync(0, Spec.Passed());

        var result = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(1), fingerprint: new string('9', 64));

        Assert.Equal(AgentOutcome.SourceChanged, result.Value.Outcome);
    }

    [Fact]
    public async Task Non_semantic_outcomes_are_recorded_even_when_the_evidence_changed_and_are_not_reclassified()
    {
        var (scene, attempt) = await DispatchedAsync();
        await scene.AddExecutionAsync(0, Spec.Failed("n", "n"));

        var result = await RecordAsync(scene, attempt, AgentOutcome.InvalidStructuredOutput, null);

        Assert.Equal(AgentOutcome.InvalidStructuredOutput, result.Value.Outcome);
    }

    [Fact]
    public async Task A_stale_response_records_the_truthful_process_evidence_even_without_artifacts()
    {
        var (scene, attempt) = await DispatchedAsync();
        await scene.AddExecutionAsync(0, Spec.Failed("n", "n"));

        var result = await RecordAsync(scene, attempt, AgentOutcome.DiagnosisEscalated, Escalation());

        Assert.Equal(AgentOutcome.VerificationEvidenceChanged, result.Value.Outcome);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Artifacts.Where(a => a.AttemptId == attempt.Id));
        Assert.Equal(TestProcessEvidence.CleanExit, (await verify.Attempts.SingleAsync(a => a.Id == attempt.Id)).GetAgentProcessExecutionEvidence());
    }

    // ---- Process proof of the requested outcome (validated before any reclassification) -----------------------------------

    private static AgentProcessEvidence? Evidence(string kind) => kind switch
    {
        "missing" => null,
        "nonzero" => new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 1, TimeSpan.FromSeconds(2)),
        "timeout" => new AgentProcessEvidence(ProcessExecutionOutcome.TimedOut, null, TimeSpan.FromSeconds(2)),
        "cancelled" => new AgentProcessEvidence(ProcessExecutionOutcome.Cancelled, null, TimeSpan.FromSeconds(2)),
        _ => TestProcessEvidence.ReportedCleanExit,
    };

    private Task<Devalente.Shared.Results.Result<RecordVerificationDiagnosisResultCommandResult>> RecordWithEvidenceAsync(
        DiagnosisTestScene scene, Attempt attempt, AgentOutcome outcome, ValidatedVerificationDiagnosis diagnosis,
        AgentProcessEvidence? evidence, string? fingerprint)
    {
        var db = _fixture.CreateContext();
        var handler = new RecordVerificationDiagnosisResultCommandHandler(db, new FixedTimeProvider(Now.AddMinutes(1)));
        return handler.HandleAsync(
            new RecordVerificationDiagnosisResultCommand(
                scene.Run.Id, attempt.Id, outcome, fingerprint, NoArtifacts, diagnosis, null, evidence, null),
            CancellationToken.None);
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("missing", true)]
    [InlineData("nonzero", false)]
    [InlineData("nonzero", true)]
    [InlineData("timeout", false)]
    [InlineData("timeout", true)]
    [InlineData("cancelled", false)]
    [InlineData("cancelled", true)]
    public async Task Unproven_semantic_requests_are_refused_without_mutation_even_after_verification_and_git_drift(string kind, bool escalation)
    {
        var (scene, attempt) = await DispatchedAsync();
        await scene.AddExecutionAsync(0, Spec.Failed("newer", "newer"));
        var before = await scene.CountsAsync();

        var result = await RecordWithEvidenceAsync(
            scene, attempt, escalation ? AgentOutcome.DiagnosisEscalated : AgentOutcome.DiagnosisFindingsRecorded,
            escalation ? Escalation() : Findings(2), Evidence(kind), new string('9', 64));

        Assert.True(result.IsFailure);
        await AssertUntouchedAsync(scene, attempt, before);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_valid_clean_exit_semantic_request_is_still_downgraded_truthfully_by_drift(bool verificationDrift, bool gitDrift)
    {
        var (scene, attempt) = await DispatchedAsync();
        if (verificationDrift)
        {
            await scene.AddExecutionAsync(0, Spec.Failed("newer", "newer"));
        }

        var result = await RecordWithEvidenceAsync(
            scene, attempt, AgentOutcome.DiagnosisFindingsRecorded, Findings(1), TestProcessEvidence.ReportedCleanExit,
            gitDrift ? new string('9', 64) : scene.Implementation.ReviewFingerprint);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(verificationDrift || gitDrift, result.Value.Outcome != AgentOutcome.DiagnosisFindingsRecorded);
        Assert.Equal(!(verificationDrift || gitDrift), verify.CollaborationMessages.Any(m => m.AttemptId == attempt.Id));
        Assert.Equal(TestProcessEvidence.CleanExit, (await verify.Attempts.SingleAsync(a => a.Id == attempt.Id)).GetAgentProcessExecutionEvidence());
    }
}
