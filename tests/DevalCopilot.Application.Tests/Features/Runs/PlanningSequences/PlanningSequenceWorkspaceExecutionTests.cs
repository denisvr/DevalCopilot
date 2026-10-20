using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Commands.ResolvePlanningSequenceStepPreparation;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.FaultInjectingDbContext;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;

/// <summary>
/// ADR-0057 at the coordinated planning claims (ADR-0055): a step is a new Agent attempt on the workspace, so it is admitted only into an
/// Idle workspace. A competing owner refuses the claim before any Git capture or sealing, and an owner that wins in the window before the
/// claim transaction refuses it at the seam; both leave the reserved ticket and its step as reserved, with no attempt, artifact, budget
/// slot or step link consumed. The sequence's own existing refusal rules then decide what happens to the preparation.
/// </summary>
public sealed class PlanningSequenceWorkspaceExecutionTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task AssertNothingConsumedAsync(PlanningSequenceTestScene scene, Guid sequenceId, int sealedDeletions)
    {
        Assert.Empty(await scene.ReadAttemptsAsync());
        var step = Assert.Single(await scene.ReadStepsAsync(sequenceId));
        Assert.Equal(PlanningSequenceStepState.Reserved, step.State);
        Assert.Null(step.AttemptId);
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.Artifacts.AsNoTracking().Where(artifact => artifact.RunId == scene.RunId).ToListAsync());
        Assert.Equal(0, await scene.CountEventsAsync(RunEventType.PlanningSequenceStepClaimed));
        Assert.Equal(sealedDeletions, scene.Artifacts.DeletedSealedFiles.Count);
    }

    // A verification execution cannot even be recorded while a sequence holds the workspace (its own database guard, ADR-0055), so the
    // competing owner of a coordinated claim is an Agent attempt of another run, which that guard does not scope.
    [Fact]
    public async Task A_competing_owner_refuses_the_coordinated_claim_before_any_sealing_and_consumes_nothing()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await WorkspaceExecutionRaceSupport.AddAsync(fixture, scene.RunId, CompetingOwner.AgentOfAnotherRun);

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        Assert.Equal(WorkspaceExecutionErrors.AgentAttemptActiveCode, Assert.Single(claim.Errors).Code);
        Assert.Equal(0, scene.Artifacts.Seals);
        await AssertNothingConsumedAsync(scene, armed.SequenceId, sealedDeletions: 0);
    }

    [Fact]
    public async Task A_competing_owner_that_wins_before_the_claim_transaction_refuses_the_coordinated_claim_at_the_seam()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner)
        {
            BeforeBeginTransaction = _ => WorkspaceExecutionRaceSupport.AddAsync(fixture, scene.RunId, CompetingOwner.AgentOfAnotherRun),
        };

        var claim = await scene.ClaimAsync(ticket, db: faulting);

        Assert.True(claim.IsFailure);
        Assert.Equal(WorkspaceExecutionErrors.AgentAttemptActiveCode, Assert.Single(claim.Errors).Code);
        Assert.Equal(1, scene.Artifacts.Seals);
        await AssertNothingConsumedAsync(scene, armed.SequenceId, sealedDeletions: 1);
    }

    [Fact]
    public async Task A_refused_coordinated_claim_is_resolved_by_the_sequences_own_preparation_rules_not_by_a_retry()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await WorkspaceExecutionRaceSupport.AddAsync(fixture, scene.RunId, CompetingOwner.AgentOfAnotherRun);

        var refused = await scene.ClaimAsync(ticket);
        Assert.True(refused.IsFailure);
        await using var db = fixture.CreateContext();
        var resolved = await new ResolvePlanningSequenceStepPreparationCommandHandler(db, scene.StepProbe, scene.Clock).HandleAsync(
            new ResolvePlanningSequenceStepPreparationCommand(ticket, Assert.Single(refused.Errors).Code), CancellationToken.None);

        Assert.True(resolved.IsSuccess);
        Assert.True(resolved.Value.Changed);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Empty(await scene.ReadAttemptsAsync());
        Assert.Equal(PlanningSequenceStepState.Revoked, Assert.Single(await scene.ReadStepsAsync(armed.SequenceId)).State);
    }
}
