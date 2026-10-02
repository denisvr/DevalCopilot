using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The ONE shared review-correction allowance (ADR-0018): ordinary and diagnosis-origin ReviewCorrection attempts count
/// together in both directions, and exhaustion at a diagnosis records exactly one idempotent Orchestrator escalation bound to
/// that diagnosis with no Attempt, no manifest, and no authorization read or consumed. The ordinary review's extra-correction
/// grants can never authorize a diagnosis-origin correction.
/// </summary>
public sealed class DiagnosisCorrectionSharedAllowanceTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static string Code<T>(Devalente.Shared.Results.Result<T> result) => Assert.Single(result.Errors).Code;

    private sealed class RecordingNotifier : IRunEventNotifier
    {
        public List<(Guid RunId, long Sequence)> Notifications { get; } = [];

        public Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken)
        {
            Notifications.Add((runId, latestSequence));
            return Task.CompletedTask;
        }
    }

    private async Task<(DiagnosisTestScene Scene, Attempt Diagnosis)> SceneWithDiagnosisAsync(int priorCorrections)
    {
        // A generous time ceiling: this class exercises the allowance, never the independent time budget.
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], maximumAgentInvocationTime: TimeSpan.FromHours(48));
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, 2)).Attempt;
        await scene.AddCorrectionHistoryAsync(priorCorrections);
        return (scene, diagnosis);
    }

    // ---- Direction one: earlier corrections consume what a diagnosis-origin one needs ---------------------------------------

    [Fact]
    public async Task One_prior_correction_of_a_default_two_leaves_exactly_one_diagnosis_origin_claim()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(priorCorrections: 1);

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
    }

    [Fact]
    public async Task Ordinary_or_failed_prior_corrections_exhaust_the_allowance_for_a_diagnosis_origin_claim()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(priorCorrections: 2);
        var attemptsBefore = (await scene.CountsAsync()).Attempts;

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        var escalated = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(result.Value);
        Assert.NotEqual(Guid.Empty, escalated.EscalationId);
        Assert.Equal(attemptsBefore, (await scene.CountsAsync()).Attempts);
        Assert.Equal(0, scene.Store.SealCount);
    }

    [Fact]
    public async Task A_corrected_implementation_already_consumed_one_slot_of_the_allowance()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed()], corrected: true, maximumAgentInvocationTime: TimeSpan.FromHours(48));
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded)).Attempt;
        var first = await scene.ClaimCorrectionAsync(diagnosis.Id);
        Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(first.Value);
        await scene.SqlAsync("UPDATE attempts SET Status = 'Failed', AgentOutcome = 'ProviderInvocationFailed' WHERE AgentResponseContract = 'ReviewCorrection' AND Status = 'Running'");

        var second = await scene.ClaimCorrectionAsync(diagnosis.Id);

        // Seeded correction (1) + the first diagnosis-origin claim (2) spend the default two.
        Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(second.Value);
    }

    // ---- Direction two: a diagnosis-origin correction consumes what an ordinary one needs ----------------------------------

    [Fact]
    public async Task Diagnosis_origin_corrections_consume_the_allowance_an_ordinary_correction_needs()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], maximumAgentInvocationTime: TimeSpan.FromHours(48));
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded)).Attempt;
        var review = (await scene.AddOrdinaryChangesRequestedReviewAsync()).Attempt;

        // Two diagnosis-origin claims (each concluded as failed so the run is free) spend the whole allowance.
        for (var index = 0; index < 2; index++)
        {
            var claim = await scene.ClaimCorrectionAsync(diagnosis.Id);
            Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(claim.Value);
            await scene.SqlAsync("UPDATE attempts SET Status = 'Failed', AgentOutcome = 'ProviderInvocationFailed' WHERE AgentResponseContract = 'ReviewCorrection' AND Status = 'Running'");
        }

        var ordinary = await scene.OrdinaryCorrectionHandler(_fixture.CreateContext()).HandleAsync(
            new CreateReviewCorrectionAttemptCommand(scene.Run.Id, review.Id), CancellationToken.None);

        var escalated = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.Escalated>(ordinary.Value);
        await using var verify = _fixture.CreateContext();
        var ordinaryEscalation = Assert.Single(await verify.ReviewCorrectionEscalations.ToListAsync());
        Assert.Equal(escalated.EscalationId, ordinaryEscalation.Id);
        Assert.Equal(review.Id, ordinaryEscalation.ImplementationReviewAttemptId);
        Assert.Empty(verify.DiagnosisCorrectionEscalations);
    }

    [Fact]
    public async Task One_diagnosis_origin_correction_leaves_exactly_one_ordinary_correction()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], maximumAgentInvocationTime: TimeSpan.FromHours(48));
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded)).Attempt;
        var review = (await scene.AddOrdinaryChangesRequestedReviewAsync()).Attempt;
        Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>((await scene.ClaimCorrectionAsync(diagnosis.Id)).Value);
        await scene.SqlAsync("UPDATE attempts SET Status = 'Failed', AgentOutcome = 'ProviderInvocationFailed' WHERE AgentResponseContract = 'ReviewCorrection' AND Status = 'Running'");

        var ordinary = await scene.OrdinaryCorrectionHandler(_fixture.CreateContext()).HandleAsync(
            new CreateReviewCorrectionAttemptCommand(scene.Run.Id, review.Id), CancellationToken.None);

        Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(ordinary.Value);
    }

    // ---- Exhaustion: one idempotent escalation ---------------------------------------------------------------------------

    [Fact]
    public async Task Exhaustion_records_one_orchestrator_escalation_to_the_human_replying_to_the_report_and_one_event_and_no_attempt()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(priorCorrections: 2);
        var notifier = new RecordingNotifier();
        var before = await scene.CountsAsync();

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, notifier: notifier);

        var escalated = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(result.Value);
        var after = await scene.CountsAsync();
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.Artifacts, after.Artifacts);
        Assert.Equal(before.Inputs, after.Inputs);
        Assert.Equal(before.Messages + 1, after.Messages);
        Assert.Equal(before.Events + 1, after.Events);
        Assert.Equal(0, scene.Store.SealCount);
        await using var verify = _fixture.CreateContext();
        var escalation = Assert.Single(await verify.DiagnosisCorrectionEscalations.ToListAsync());
        Assert.Equal(escalated.EscalationId, escalation.Id);
        Assert.Equal(escalated.EscalationMessageId, escalation.CollaborationMessageId);
        Assert.Equal(diagnosis.Id, escalation.VerificationDiagnosisAttemptId);
        Assert.Equal(scene.Run.Id, escalation.RunId);
        var message = await verify.CollaborationMessages.SingleAsync(m => m.Id == escalation.CollaborationMessageId);
        Assert.Equal(CollaborationMessageType.Escalation, message.Type);
        Assert.Equal(ParticipantIdentity.ForOrchestrator(), message.Actor);
        Assert.Equal(ParticipantIdentity.ForHuman(), message.Recipient);
        Assert.Equal(CollaborationMessageProvenance.HostConstructed, message.Provenance);
        Assert.Equal(scene.ReportId, message.InReplyToMessageId);
        Assert.Null(message.AttemptId);
        var recorded = await verify.Events.SingleAsync(e => e.RunId == scene.Run.Id && e.PayloadJson.Contains(message.Id.ToString()));
        Assert.Equal(RunEventType.CollaborationMessageRecorded, recorded.EventType);
        Assert.Equal(escalated.LatestEventSequence, recorded.Sequence);
        Assert.Equal([(scene.Run.Id, recorded.Sequence)], notifier.Notifications);
        Assert.Empty(verify.ReviewCorrectionEscalations);
        Assert.Empty(verify.ReviewCorrectionAuthorizations);
    }

    [Fact]
    public async Task A_repeated_request_returns_the_same_escalation_and_creates_nothing_more()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(priorCorrections: 2);
        var notifier = new RecordingNotifier();
        var first = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(
            (await scene.ClaimCorrectionAsync(diagnosis.Id, notifier: notifier)).Value);
        var afterFirst = await scene.CountsAsync();

        var second = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(
            (await scene.ClaimCorrectionAsync(diagnosis.Id, notifier: notifier)).Value);

        Assert.Equal(first.EscalationId, second.EscalationId);
        Assert.Equal(first.EscalationMessageId, second.EscalationMessageId);
        Assert.Equal(first.LatestEventSequence, second.LatestEventSequence);
        Assert.Equal(afterFirst, await scene.CountsAsync());
        Assert.Equal(2, notifier.Notifications.Count);
        Assert.All(notifier.Notifications, n => Assert.Equal(first.LatestEventSequence, n.Sequence));
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.DiagnosisCorrectionEscalations.ToListAsync());
    }

    [Fact]
    public async Task A_concurrent_identical_request_that_wins_the_insert_leaves_one_escalation_and_the_loser_returns_it()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(priorCorrections: 2);
        CreateDiagnosisCorrectionAttemptCommandResult.Escalated? winner = null;
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = async _ =>
            {
                winner = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(
                    (await scene.ClaimCorrectionAsync(diagnosis.Id)).Value);
            },
        };

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, faulting);

        var loser = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(result.Value);
        Assert.NotNull(winner);
        Assert.Equal(winner.EscalationId, loser.EscalationId);
        Assert.Equal(winner.EscalationMessageId, loser.EscalationMessageId);
        Assert.Equal(winner.LatestEventSequence, loser.LatestEventSequence);
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.DiagnosisCorrectionEscalations.ToListAsync());
        Assert.Single(await verify.CollaborationMessages.Where(m => m.RunId == scene.Run.Id && m.Type == CollaborationMessageType.Escalation && m.AttemptId == null).ToListAsync());
        Assert.Single(await verify.Events.Where(e => e.RunId == scene.Run.Id && e.PayloadJson.Contains(loser.EscalationMessageId.ToString())).ToListAsync());
    }

    [Fact]
    public async Task Each_diagnosis_has_its_own_escalation()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(priorCorrections: 2);
        Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>((await scene.ClaimCorrectionAsync(diagnosis.Id)).Value);
        // A second findings diagnosis of the same identity can only exist once the first is no longer a success; here a
        // different (un-deduplicated) ordered selection is used.
        await scene.AddExecutionAsync(0, Spec.Passed());
        var other = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded)).Attempt;

        var second = await scene.ClaimCorrectionAsync(other.Id);

        Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(second.Value);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(2, await verify.DiagnosisCorrectionEscalations.CountAsync());
    }

    [Fact]
    public async Task The_escalation_is_recorded_only_for_an_applicable_findings_diagnosis()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(priorCorrections: 2);
        await scene.AddExecutionAsync(1, Spec.Passed());

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        Assert.Equal(CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode, Code(result));
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.DiagnosisCorrectionEscalations);
    }

    // ---- Ordinary grants cannot authorize a diagnosis-origin correction ---------------------------------------------------

    [Fact]
    public async Task An_ordinary_extra_correction_grant_never_authorizes_a_diagnosis_origin_correction_and_is_never_consumed()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(priorCorrections: 2);
        var review = (await scene.AddOrdinaryChangesRequestedReviewAsync()).Attempt;
        var (ordinaryEscalation, authorization) = await scene.AddOrdinaryGrantAsync(review);
        Assert.True(authorization.IsAvailable);
        var attemptsBefore = (await scene.CountsAsync()).Attempts;

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(result.Value);
        Assert.Equal(attemptsBefore, (await scene.CountsAsync()).Attempts);
        await using var verify = _fixture.CreateContext();
        var storedAuthorization = await verify.ReviewCorrectionAuthorizations.AsNoTracking().SingleAsync();
        Assert.True(storedAuthorization.IsAvailable);
        Assert.Null(storedAuthorization.ConsumedByAttemptId);
        Assert.Equal(ordinaryEscalation.Id, (await verify.ReviewCorrectionEscalations.AsNoTracking().SingleAsync()).Id);
        Assert.Single(await verify.DiagnosisCorrectionEscalations.ToListAsync());
    }

    [Fact]
    public async Task The_same_ordinary_grant_still_authorizes_the_ordinary_correction_it_belongs_to()
    {
        var (scene, _) = await SceneWithDiagnosisAsync(priorCorrections: 2);
        var review = (await scene.AddOrdinaryChangesRequestedReviewAsync()).Attempt;
        var (_, authorization) = await scene.AddOrdinaryGrantAsync(review);

        var ordinary = await scene.OrdinaryCorrectionHandler(_fixture.CreateContext()).HandleAsync(
            new CreateReviewCorrectionAttemptCommand(scene.Run.Id, review.Id), CancellationToken.None);

        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(ordinary.Value);
        await using var verify = _fixture.CreateContext();
        var stored = await verify.ReviewCorrectionAuthorizations.AsNoTracking().SingleAsync(a => a.Id == authorization.Id);
        Assert.Equal(created.AttemptId, stored.ConsumedByAttemptId);
    }

    [Fact]
    public async Task A_diagnosis_origin_claim_below_the_allowance_neither_reads_nor_consumes_an_available_ordinary_grant()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(priorCorrections: 0);
        var review = (await scene.AddOrdinaryChangesRequestedReviewAsync()).Attempt;
        var (_, authorization) = await scene.AddOrdinaryGrantAsync(review);

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        await using var verify = _fixture.CreateContext();
        var stored = await verify.ReviewCorrectionAuthorizations.AsNoTracking().SingleAsync(a => a.Id == authorization.Id);
        Assert.True(stored.IsAvailable);
    }

    [Fact]
    public async Task A_consumed_ordinary_grant_slot_also_counts_in_the_shared_allowance_for_the_diagnosis()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(priorCorrections: 0);
        var review = (await scene.AddOrdinaryChangesRequestedReviewAsync()).Attempt;
        await scene.AddOrdinaryGrantAsync(review);
        // Two ordinary corrections happened (one using the extra grant): the allowance a diagnosis can draw on is gone.
        await scene.AddCorrectionHistoryAsync(2);

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(result.Value);
    }
}
