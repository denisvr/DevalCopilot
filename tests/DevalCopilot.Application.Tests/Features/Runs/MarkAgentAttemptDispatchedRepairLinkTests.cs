using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The authoritative last gate before a CriticalReviewer, Resolver, or CodeReviewer format-repair attempt may be
/// dispatched: its link, its failed source, and both exact input identities must still be one coherent fact, or no
/// provider process can start. The claim-time "latest attempt" and "no earlier repair" tests are deliberately not
/// re-applied (the repair owns that slot), the ordinary duplicate-input classifications stay, and the Planner's
/// unchanged repair has no such gate.
/// </summary>
public sealed class MarkAgentAttemptDispatchedRepairLinkTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    public enum Stage
    {
        CriticalReview,
        Resolution,
        CodeReview,
    }

    public static TheoryData<Stage> Stages => new() { Stage.CriticalReview, Stage.Resolution, Stage.CodeReview };

    private sealed record Arrangement(RepairTestScene Scene, Attempt Source, Attempt Repair, string Fingerprint);

    /// <summary>A failed source of the stage and a claimed (undispatched) repair of it that copies the source's
    /// exact recorded inputs, built with the Domain factories.</summary>
    private async Task<Arrangement> ArrangeAsync(Stage stage)
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        Attempt source;
        string fingerprint = RepairTestScene.Fingerprint;
        switch (stage)
        {
            case Stage.CriticalReview:
                source = scene.AddCriticalReviewSource().Source;
                break;
            case Stage.Resolution:
                source = scene.AddResolverSource().Source;
                break;
            default:
                var implementation = scene.AddInitialImplementation();
                var verification = await scene.AddPassedVerificationAsync(implementation);
                source = scene.AddInvalidCodeReview(implementation, verification);
                fingerprint = implementation.ReviewFingerprint;
                break;
        }

        await scene.SaveAsync();
        var repair = await scene.AddRepairAsync(source);
        scene.Detach();
        return new Arrangement(scene, source, repair, fingerprint);
    }

    private async Task<Devalente.Shared.Results.Result<DateTimeOffset>> DispatchAsync(Arrangement arrangement)
    {
        await using var context = _fixture.CreateContext();
        var result = await new MarkAgentAttemptDispatchedCommandHandler(context, new FixedTimeProvider(RepairTestScene.Now.AddMinutes(1)))
            .HandleAsync(
                new MarkAgentAttemptDispatchedCommand(arrangement.Scene.Run.Id, arrangement.Repair.Id), CancellationToken.None);
        if (result.IsSuccess)
        {
            await context.SaveChangesAsync();
        }

        return result;
    }

    private async Task AssertNotDispatchedAsync(Arrangement arrangement)
    {
        await using var verify = _fixture.CreateContext();
        var repair = await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == arrangement.Repair.Id);
        Assert.Null(repair.AgentDispatchedAtUtc);
        Assert.Equal(AttemptStatus.Running, repair.Status);
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task A_coherent_repair_dispatches_even_though_it_is_no_longer_a_candidate_for_the_claim_time_tests(Stage stage)
    {
        var arrangement = await ArrangeAsync(stage);
        // A later attempt exists and the source has a repair: exactly the state the claim-time tests refuse,
        // but the repair itself owns the slot, so dispatch must not re-apply them.
        var laterNumber = arrangement.Scene.Lineage.ReserveAttemptNumber();
        var later = Attempt.ClaimAgent(
            Guid.NewGuid(), arrangement.Scene.Run.Id, laterNumber, arrangement.Scene.Workspace.Id,
            arrangement.Repair.AgentGitCheckpointId!.Value, arrangement.Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, RepairTestScene.Now, laterNumber);
        later.Fail(RepairTestScene.Now);
        arrangement.Scene.Db.Attempts.Add(later);
        await arrangement.Scene.SaveAsync();

        var result = await DispatchAsync(arrangement);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        await using var verify = _fixture.CreateContext();
        Assert.NotNull((await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == arrangement.Repair.Id)).AgentDispatchedAtUtc);
    }

    public static TheoryData<Stage, string, string> IncoherentLinks => new()
    {
        { Stage.CriticalReview, "source outcome changed", "SOURCE:AgentOutcome = 'ProviderInvocationFailed'" },
        { Stage.CriticalReview, "source completed", "SOURCE:Status = 'Completed', AgentOutcome = 'Accepted'" },
        { Stage.CriticalReview, "source process evidence absent", "SOURCE:AgentProcessOutcome = NULL, AgentProcessExitCode = NULL, AgentProcessDuration = NULL" },
        { Stage.CriticalReview, "source adapter version", "SOURCE:AgentAdapterContractVersion = 'claude-critical-review-v0'" },
        { Stage.CriticalReview, "source not dispatched", "SOURCE:AgentDispatchedAtUtc = NULL" },
        { Stage.CriticalReview, "source unreadable", "SOURCE:AgentOutcome = 'NoSuchOutcome'" },
        { Stage.CriticalReview, "repair adapter version", "REPAIR:AgentAdapterContractVersion = 'claude-critical-review-v0'" },
        { Stage.CriticalReview, "repair permission profile", "REPAIR:AgentPermissionProfile = 'WorkspaceEditOnly'" },
        { Stage.CriticalReview, "repair checkpoint differs", "REPAIR:AgentCheckpointFingerprintSha256 = 'ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff'" },
        { Stage.CriticalReview, "link to itself", "REPAIR:AgentRepairSourceAttemptId = Id" },
        { Stage.Resolution, "source outcome changed", "SOURCE:AgentOutcome = 'ProviderInvocationFailed'" },
        { Stage.Resolution, "source process evidence non-clean", "SOURCE:AgentProcessExitCode = 2" },
        { Stage.Resolution, "source profile", "SOURCE:AgentPermissionProfile = 'WorkspaceEditOnly'" },
        { Stage.Resolution, "repair adapter version", "REPAIR:AgentAdapterContractVersion = 'codex-challenge-resolution-v0'" },
        { Stage.Resolution, "link to itself", "REPAIR:AgentRepairSourceAttemptId = Id" },
        { Stage.CodeReview, "source outcome changed", "SOURCE:AgentOutcome = 'ProviderInvocationFailed'" },
        { Stage.CodeReview, "source adapter version", "SOURCE:AgentAdapterContractVersion = 'codex-implementation-review-v0'" },
        { Stage.CodeReview, "repair adapter version", "REPAIR:AgentAdapterContractVersion = 'codex-implementation-review-v0'" },
        { Stage.CodeReview, "link to itself", "REPAIR:AgentRepairSourceAttemptId = Id" },
    };

    [Theory]
    [MemberData(nameof(IncoherentLinks))]
    public async Task An_incoherent_repair_link_source_or_tuple_is_refused_before_any_provider_process(
        Stage stage, string description, string change)
    {
        var arrangement = await ArrangeAsync(stage);
        var (target, assignments) = change.Split(':', 2) is [var which, var sql] ? (which, sql) : throw new InvalidOperationException();
        await arrangement.Scene.CorruptAsync(target == "SOURCE" ? arrangement.Source.Id : arrangement.Repair.Id, assignments);

        var result = await DispatchAsync(arrangement);

        Assert.True(result.IsFailure, description);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.InvalidRepairLinkCode, result.Errors[0].Code);
        await AssertNotDispatchedAsync(arrangement);
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task A_source_that_recorded_a_semantic_message_is_refused(Stage stage)
    {
        var arrangement = await ArrangeAsync(stage);
        await using (var context = _fixture.CreateContext())
        {
            var inReplyTo = (await context.AttemptInputMessages.AsNoTracking().Where(i => i.AttemptId == arrangement.Source.Id)
                .OrderBy(i => i.Sequence).FirstAsync()).CollaborationMessageId;
            var (type, actor, recipient, content) = stage switch
            {
                Stage.CriticalReview => (CollaborationMessageType.Acceptance,
                    ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode),
                    ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
                    JsonSerializer.Serialize(new { rationale = "Sound." })),
                Stage.Resolution => (CollaborationMessageType.Decision,
                    ParticipantIdentity.ForAgent(AgentRole.Resolver, AgentProvider.Codex),
                    ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                    JsonSerializer.Serialize(new { resolution = "accepted", rationale = "Why", resultingPlanChanges = "None", nextAction = "None" })),
                _ => (CollaborationMessageType.ReviewFinding,
                    ParticipantIdentity.ForAgent(AgentRole.CodeReviewer, AgentProvider.Codex),
                    ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                    JsonSerializer.Serialize(new { severity = "high", category = "correctness", evidence = "E", requiredChange = "C" })),
            };
            context.CollaborationMessages.Add(CollaborationMessage.Record(
                Guid.NewGuid(), arrangement.Scene.Run.Id, arrangement.Source.Id, CollaborationMessage.ProtocolVersionOne, actor, recipient,
                type, inReplyTo, "A message.", content, CollaborationMessageProvenance.ProviderObserved, RepairTestScene.Now));
            await context.SaveChangesAsync();
        }

        var result = await DispatchAsync(arrangement);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.InvalidRepairLinkCode, result.Errors[0].Code);
        await AssertNotDispatchedAsync(arrangement);
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task A_repair_whose_recorded_inputs_no_longer_equal_the_sources_is_refused(Stage stage)
    {
        var arrangement = await ArrangeAsync(stage);
        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM attempt_input_messages WHERE AttemptId = {arrangement.Repair.Id} AND Sequence = (SELECT MAX(Sequence) FROM attempt_input_messages WHERE AttemptId = {arrangement.Repair.Id}) AND (SELECT COUNT(*) FROM attempt_input_messages WHERE AttemptId = {arrangement.Repair.Id}) > 1");
            if (stage != Stage.Resolution)
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM attempt_input_messages WHERE AttemptId = {arrangement.Repair.Id}");
            }
        }

        var result = await DispatchAsync(arrangement);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.InvalidRepairLinkCode, result.Errors[0].Code);
        await AssertNotDispatchedAsync(arrangement);
    }

    [Fact]
    public async Task A_code_review_repair_whose_verification_set_differs_from_the_sources_is_refused()
    {
        var arrangement = await ArrangeAsync(Stage.CodeReview);
        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM attempt_verification_evidence WHERE AttemptId = {arrangement.Repair.Id} AND Sequence = 1");
        }

        var result = await DispatchAsync(arrangement);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.InvalidRepairLinkCode, result.Errors[0].Code);
        await AssertNotDispatchedAsync(arrangement);
    }

    [Fact]
    public async Task A_code_review_repair_with_reordered_verification_rows_is_refused()
    {
        var arrangement = await ArrangeAsync(Stage.CodeReview);
        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempt_verification_evidence SET Sequence = 5 WHERE AttemptId = {arrangement.Repair.Id} AND Sequence = 0");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempt_verification_evidence SET Sequence = 0 WHERE AttemptId = {arrangement.Repair.Id} AND Sequence = 1");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempt_verification_evidence SET Sequence = 1 WHERE AttemptId = {arrangement.Repair.Id} AND Sequence = 5");
        }

        var result = await DispatchAsync(arrangement);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.InvalidRepairLinkCode, result.Errors[0].Code);
        await AssertNotDispatchedAsync(arrangement);
    }

    [Fact]
    public async Task A_repair_linked_to_a_source_of_another_run_is_refused()
    {
        var arrangement = await ArrangeAsync(Stage.CriticalReview);
        var other = await RepairTestScene.CreateAsync(_fixture);
        var (foreignSource, _) = other.AddCriticalReviewSource();
        await other.SaveAsync();
        await arrangement.Scene.CorruptAsync(arrangement.Repair.Id, $"AgentRepairSourceAttemptId = '{foreignSource.Id.ToString().ToUpperInvariant()}'");

        var result = await DispatchAsync(arrangement);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.InvalidRepairLinkCode, result.Errors[0].Code);
        await AssertNotDispatchedAsync(arrangement);
    }

    [Fact]
    public async Task The_ordinary_duplicate_input_classification_is_preserved_for_a_coherent_repair()
    {
        var arrangement = await ArrangeAsync(Stage.CriticalReview);
        // Another critical review already completed successfully for the very same Proposal.
        var proposalId = (await arrangement.Scene.Db.AttemptInputMessages.AsNoTracking()
            .SingleAsync(i => i.AttemptId == arrangement.Source.Id)).CollaborationMessageId;
        var number = arrangement.Scene.Lineage.ReserveAttemptNumber();
        var competing = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), arrangement.Scene.Run.Id, number, arrangement.Scene.Workspace.Id, arrangement.Scene.Checkpoint.Id,
            RepairTestScene.Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, RepairTestScene.Now, number);
        competing.MarkAgentDispatched(RepairTestScene.Now);
        competing.CompleteAgent(
            AgentOutcome.Accepted, RepairTestScene.Fingerprint, RepairTestScene.Now, processEvidence: TestProcessEvidence.CleanExit);
        arrangement.Scene.Db.Attempts.Add(competing);
        arrangement.Scene.Db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), competing.Id, proposalId, sequence: 0));
        await arrangement.Scene.SaveAsync();

        var result = await DispatchAsync(arrangement);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.InputAlreadyReviewedCode, result.Errors[0].Code);
        await AssertNotDispatchedAsync(arrangement);
    }

    [Fact]
    public async Task The_planner_repair_has_no_such_gate_and_dispatches_as_before()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var number = scene.Lineage.ReserveAttemptNumber();
        var source = Attempt.ClaimAgent(
            Guid.NewGuid(), scene.Run.Id, number, scene.Workspace.Id, scene.Checkpoint.Id, RepairTestScene.Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, RepairTestScene.Now, number);
        RepairTestScene.Invalidate(source, RepairTestScene.Fingerprint);
        scene.Db.Attempts.Add(source);
        var repairNumber = scene.Lineage.ReserveAttemptNumber();
        var repair = Attempt.ClaimAgentPlanningRepair(
            Guid.NewGuid(), scene.Run.Id, repairNumber, scene.Workspace.Id, scene.Checkpoint.Id, RepairTestScene.Fingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, RepairTestScene.Now, null, null, repairNumber, source.Id);
        scene.Db.Attempts.Add(repair);
        await scene.SaveAsync();
        // Even a source that is no longer coherent does not stop the unchanged Planner repair here.
        await scene.CorruptAsync(source.Id, "AgentOutcome = 'ProviderInvocationFailed'");

        await using var context = _fixture.CreateContext();
        var result = await new MarkAgentAttemptDispatchedCommandHandler(context, new FixedTimeProvider(RepairTestScene.Now))
            .HandleAsync(new MarkAgentAttemptDispatchedCommand(scene.Run.Id, repair.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
    }
}
