using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueue;

/// <summary>
/// Whole queued cycles through the production claims, dispatch markers and result recorders over real SQLite (ADR-0062). Every scenario
/// is driven twice, in separate databases: once Immediate (ADR-0060) and once queued, and the queued cycle must reach exactly the same end,
/// reason, ordered trace of kinds, passes and recipes and the same escalation: queueing only delays, it never changes what the cycle does.
/// Each queued step additionally has exactly one admission that was reserved, consumed by its own claim and ordered after every earlier one.
/// </summary>
public sealed class LocalExecutionQueueCycleTests
{
    public static TheoryData<string> Scenarios =>
    [
        "approval",
        "review-correction",
        "diagnosis-correction",
        "mixed-origins",
        "review-exhaustion",
        "diagnosis-exhaustion",
        "provider-escalation",
        "implementation-without-exit",
        "verification-timed-out",
        "review-failed",
    ];

    private static (int Recipes, int Corrections, LocalExecutionStepScript Script) Scenario(string name) => name switch
    {
        "approval" => (2, 2, new LocalExecutionStepScript()),
        "review-correction" => (2, 2, new LocalExecutionStepScript { Findings = 3 }.On(Step.CodeReview, 1, ReviewChangesRequested)),
        "diagnosis-correction" => (3, 2, new LocalExecutionStepScript { Findings = 2 }.Verify(1, 0, VerificationFailed)),
        "mixed-origins" => (1, 2, new LocalExecutionStepScript().Verify(1, 0, VerificationFailed).On(Step.CodeReview, 2, ReviewChangesRequested)),
        "review-exhaustion" => (1, 2, new LocalExecutionStepScript().On(Step.CodeReview, ReviewChangesRequested)),
        "diagnosis-exhaustion" => (1, 1, new LocalExecutionStepScript().Verify(1, 0, VerificationFailed).Verify(2, 0, VerificationFailed)),
        "provider-escalation" => (2, 2, new LocalExecutionStepScript().Verify(1, 1, VerificationFailed).On(Step.VerificationDiagnosis, DiagnosisEscalated)),
        "implementation-without-exit" => (2, 2, new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedNoExit)),
        "verification-timed-out" => (2, 2, new LocalExecutionStepScript().Verify(1, 0, VerificationFailed).Verify(1, 1, VerificationTimedOut)),
        "review-failed" => (1, 2, new LocalExecutionStepScript().On(Step.CodeReview, ProviderFailedWithExit)),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task A_queued_cycle_reaches_exactly_the_end_trace_and_escalation_of_the_immediate_cycle(string name)
    {
        var (recipes, corrections, script) = Scenario(name);

        var immediate = await DriveImmediateAsync(recipes, corrections, Scenario(name).Script);
        var queued = await DriveQueuedAsync(recipes, corrections, script);

        Assert.Equal(immediate.State, queued.State);
        Assert.Equal(immediate.Reason, queued.Reason);
        Assert.Equal(immediate.Trace, queued.Trace);
        Assert.Equal(immediate.Escalations, queued.Escalations);
        Assert.Equal(immediate.Provenance, queued.Provenance);
    }

    private sealed record Outcome(
        LocalExecutionSequenceState State,
        string? Reason,
        List<(Step Kind, int Pass, int? Recipe)> Trace,
        int Escalations,
        string? Provenance);

    private static async Task<Outcome> DriveImmediateAsync(int recipes, int corrections, LocalExecutionStepScript script)
    {
        var fixture = new SqliteDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipes, maximumReviewCorrectionAttempts: corrections);
            var plan = await scene.AddAcceptedRootPlanAsync();
            var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);
            var escalations = await scene.ReadMessagesAsync(CollaborationMessageType.Escalation);
            return new Outcome(
                sequence.State, sequence.ReasonCode, await scene.TraceAsync(), escalations.Count, escalations.SingleOrDefault()?.Provenance.ToString());
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private static async Task<Outcome> DriveQueuedAsync(int recipes, int corrections, LocalExecutionStepScript script)
    {
        var fixture = new SqliteDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipes, maximumReviewCorrectionAttempts: corrections);
            var plan = await scene.AddAcceptedRootPlanAsync();
            await scene.QueueAsync(plan.ProposalId);
            var host = new LocalExecutionQueueHost((scene, script));

            await host.RunUntilAllEndedAsync();

            var sequence = await scene.ReadSequenceAsync();
            var steps = await scene.ReadStepsAsync();
            var admissions = await scene.ReadAdmissionsAsync();

            // One admission per operation step, in the same order, each reserved with its own step and consumed by its own claim.
            var claimed = steps.Where(step => step.State != LocalExecutionSequenceStepState.Revoked).ToList();
            Assert.Equal(claimed.Count, admissions.Count);
            for (var index = 0; index < admissions.Count; index++)
            {
                Assert.Equal(LocalExecutionAdmissionState.Consumed, admissions[index].State);
                Assert.Equal(claimed[index].Id, admissions[index].StepId);
                Assert.Equal(claimed[index].Ordinal, admissions[index].Ordinal);
                Assert.Equal(claimed[index].Kind, admissions[index].Kind);
                Assert.Equal(claimed[index].PassNumber, admissions[index].PassNumber);
                Assert.Equal(claimed[index].RecipeOrdinal, admissions[index].RecipeOrdinal);
                Assert.Equal(claimed[index].SourceCheckpointId, admissions[index].SourceCheckpointId);
                Assert.Equal(claimed[index].ReportMessageId, admissions[index].ReportMessageId);
                Assert.Equal(claimed[index].SubjectAttemptId, admissions[index].SubjectAttemptId);
                Assert.Equal(claimed[index].PredecessorStepId, admissions[index].PredecessorStepId);
                Assert.Equal(index == 0 ? 1 : admissions[index - 1].AdmissionNumber + 1, index == 0 ? 1 : admissions[index].AdmissionNumber);
            }

            Assert.True(admissions.Zip(admissions.Skip(1), (earlier, later) => earlier.AdmissionNumber < later.AdmissionNumber).All(ordered => ordered));
            var escalations = await scene.ReadMessagesAsync(CollaborationMessageType.Escalation);
            return new Outcome(
                sequence.State, sequence.ReasonCode, await scene.TraceAsync(), escalations.Count, escalations.SingleOrDefault()?.Provenance.ToString());
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
