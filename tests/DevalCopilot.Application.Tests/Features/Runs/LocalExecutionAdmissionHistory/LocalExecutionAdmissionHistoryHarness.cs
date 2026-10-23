using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionAdmissionHistory;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionAdmissionHistory;

/// <summary>
/// The reusable harness of the ADR-0063 admission-history tests: the ADR-0062 real-file-backed-SQLite queued scene (every admission is
/// created and moved by the production commands), the real handler over a context of its own, and a corruption helper that writes damaged
/// storage through a separate connection. Nothing here writes through the history handler; the scene alone changes the world, exactly
/// as another request would.
/// </summary>
internal sealed class LocalExecutionAdmissionHistoryHarness
{
    public const string AdmissionImmutable = "trg_local_execution_admission_immutable";

    private readonly LocalExecutionInspectionHarness inspection;

    public LocalExecutionAdmissionHistoryHarness(LocalExecutionSequenceTestScene scene)
    {
        Scene = scene;
        inspection = new LocalExecutionInspectionHarness(scene);
    }

    public LocalExecutionSequenceTestScene Scene { get; }

    public static async Task<(LocalExecutionAdmissionHistoryHarness Harness, Guid SequenceId)> QueuedAsync(
        SqliteDatabaseFixture fixture, int recipeCount = 2, int corrections = 2)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(
            fixture, recipeCount, maximumReviewCorrectionAttempts: corrections);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var receipt = await scene.QueueAsync(plan.ProposalId);
        return (new LocalExecutionAdmissionHistoryHarness(scene), receipt.SequenceId);
    }

    /// <summary>A whole queued cycle driven by the production commands until it ends, over a fresh database of its own.</summary>
    public static async Task<(LocalExecutionAdmissionHistoryHarness Harness, Guid SequenceId)> EndedAsync(
        SqliteDatabaseFixture fixture, int recipeCount, LocalExecutionStepScript script, int corrections = 2)
    {
        var (harness, sequenceId) = await QueuedAsync(fixture, recipeCount, corrections);
        await new LocalExecutionQueueHost((harness.Scene, script)).RunUntilAllEndedAsync();
        return (harness, sequenceId);
    }

    /// <summary>Verification 1 fails (diagnosis and its correction), the second pass passes and its review requests changes (review
    /// correction), the third passes and is approved: both correction origins in one queued cycle.</summary>
    public static LocalExecutionStepScript MixedOriginsScript() => new LocalExecutionStepScript()
        .Verify(1, 0, VerificationFailed)
        .On(Step.CodeReview, 2, ReviewChangesRequested);

    public async Task<GetLocalExecutionAdmissionHistoryQueryResult> HistoryAsync(Guid? runId = null, params IInterceptor[] interceptors)
    {
        var result = await TryHistoryAsync(runId, interceptors);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    public async Task<Result<GetLocalExecutionAdmissionHistoryQueryResult>> TryHistoryAsync(
        Guid? runId = null, params IInterceptor[] interceptors)
    {
        await using var context = Scene.Fixture.CreateContext(interceptors);
        var result = await new GetLocalExecutionAdmissionHistoryQueryHandler(context).HandleAsync(
            new GetLocalExecutionAdmissionHistoryQuery(runId ?? Scene.RunId), CancellationToken.None);
        Assert.Empty(context.ChangeTracker.Entries());
        return result;
    }

    public Task<string> ChangeFingerprintAsync() => inspection.ChangeFingerprintAsync();

    public Task CorruptAsync(string sql, object?[]? values = null, params string[] triggersToDrop) =>
        inspection.CorruptAsync(sql, values, triggersToDrop);

    /// <summary>Every proved answer that is not Recorded shows nothing of the ledger.</summary>
    public static void AssertShowsNothing(GetLocalExecutionAdmissionHistoryQueryResult history, LocalExecutionAdmissionHistoryStatus status)
    {
        Assert.Equal(status, history.Status);
        Assert.Null(history.Sequence);
        Assert.Empty(history.Admissions);
    }

    /// <summary>The stored rows of a driven cycle by admission order, with every pinned recipe name.</summary>
    public async Task<(List<LocalExecutionAdmission> Admissions, List<LocalExecutionSequenceStep> Steps, List<LocalExecutionSequenceRecipe> Recipes)>
        StoredAsync() => (await Scene.ReadAdmissionsAsync(), await Scene.ReadStepsAsync(), await Scene.ReadPinnedRecipesAsync());

    /// <summary>Every ledger member equals its stored admission field by field, in ordinal order, and shows nothing the contract forbids.</summary>
    public async Task AssertMirrorsStorageAsync(GetLocalExecutionAdmissionHistoryQueryResult history)
    {
        Assert.Equal(LocalExecutionAdmissionHistoryStatus.Recorded, history.Status);
        var (admissions, _, recipes) = await StoredAsync();
        var sequence = await Scene.ReadSequenceAsync();
        Assert.Equal(admissions.Count, history.Admissions.Count);
        var header = history.Sequence!;
        Assert.Equal(sequence.Id, header.SequenceId);
        Assert.Equal(LocalExecutionAdmissionMode.WaitForAdmission, header.AdmissionMode);
        Assert.Equal(sequence.State, header.RecordedState);
        Assert.Equal(sequence.ReasonCode, header.ReasonCode);
        Assert.Equal(sequence.Revision, header.SequenceRevision);
        Assert.Equal(sequence.ArmedAtUtc, header.ArmedAtUtc);
        Assert.Equal(sequence.AdvancementDeadlineUtc, header.AdvancementDeadlineUtc);
        Assert.Equal(sequence.UpdatedAtUtc, header.UpdatedAtUtc);
        Assert.Equal(sequence.EndedAtUtc, header.EndedAtUtc);
        for (var position = 0; position < admissions.Count; position++)
        {
            var stored = admissions[position];
            var member = history.Admissions[position];
            Assert.Equal(stored.Id, member.AdmissionId);
            Assert.Equal(stored.AdmissionNumber, member.AdmissionNumber);
            Assert.Equal(position, member.Ordinal);
            Assert.Equal(stored.Ordinal, member.Ordinal);
            Assert.Equal(stored.Kind, member.Kind);
            Assert.Equal(stored.PassNumber, member.PassNumber);
            Assert.Equal(stored.RecipeOrdinal, member.RecipeOrdinal);
            Assert.Equal(stored.RecipeOrdinal is { } recipe ? recipes[recipe].CommandName : null, member.RecipeName);
            Assert.Equal(stored.State, member.RecordedState);
            Assert.Equal(stored.WaitingReason, member.WaitingReason);
            Assert.Equal(stored.WaitingReasonRecordedAtUtc, member.WaitingReasonRecordedAtUtc);
            Assert.Equal(stored.EnqueuedAtUtc, member.EnqueuedAtUtc);
            Assert.Equal(stored.UpdatedAtUtc, member.UpdatedAtUtc);
            Assert.Equal(stored.Revision, member.Revision);
            Assert.Equal(stored.ReasonCode, member.ReasonCode);
            Assert.Equal(stored.StepId, member.StepId);
        }
    }

    /// <summary>One driven tick of the queue host's duties for this scene alone.</summary>
    public Task TickAsync(LocalExecutionStepScript? script = null) =>
        new LocalExecutionQueueHost((Scene, script ?? new LocalExecutionStepScript())).TickAsync();

    /// <summary>Drives <paramref name="steps"/> whole steps one at a time (reserve, claim, dispatch, scripted result, then the observation
    /// that concludes the step and records its one waiting successor), so the cycle stops with its last admission Waiting.</summary>
    public async Task DriveStepsThenWaitAsync(Guid sequenceId, int steps, LocalExecutionStepScript? script = null)
    {
        script ??= new LocalExecutionStepScript();
        for (var step = 0; step < steps; step++)
        {
            var ticket = await Scene.ReserveAsync();
            Assert.NotNull(ticket);
            await Scene.DriveTicketAsync(ticket, script);
            var observed = await Scene.ObserveAsync(sequenceId);
            Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Errors[0].Code : null);
            Scene.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(LocalExecutionAdmissionState.Waiting, (await LastAdmissionAsync()).State);
    }

    public async Task<LocalExecutionAdmission> LastAdmissionAsync()
    {
        await using var context = Scene.Fixture.CreateContext();
        return (await context.LocalExecutionAdmissions.AsNoTracking()
            .Where(admission => context.LocalExecutionSequences.Any(sequence => sequence.Id == admission.SequenceId && sequence.RunId == Scene.RunId))
            .ToListAsync()).OrderBy(admission => admission.Ordinal).Last();
    }
}
