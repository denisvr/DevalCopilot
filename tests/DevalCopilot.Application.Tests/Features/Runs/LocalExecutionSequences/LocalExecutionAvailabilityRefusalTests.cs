using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>The availability refusal codes the hosted tests cannot seed (ADR-0060), at the query and arming-handler level: each refuses
/// in the same fixed order for the read-only availability and for arming, and writes nothing.</summary>
public sealed class LocalExecutionAvailabilityRefusalTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task AssertRefusedAsync(LocalExecutionSequenceTestScene scene, Guid planId, string code)
    {
        var availability = await scene.ReadAvailabilityAsync(planId);
        Assert.False(availability.Available);
        Assert.Equal(code, availability.RefusalCode);
        var armed = await scene.TryArmAsync(new(scene.RunId, planId, scene.WorkspaceId, scene.CheckpointId, scene.FingerprintSha256, new string('e', 64)));
        Assert.True(armed.IsFailure);
        Assert.Equal(code, armed.Errors[0].Code);
        Assert.Null(await scene.FindSequenceAsync());
    }

    [Fact]
    public async Task A_live_planning_sequence_of_the_run_refuses_with_planning_active()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        // The planning arming guard would itself refuse this run: the coexisting live sequence is fabricated to prove this refusal alone.
        await scene.ExecuteSqlAsync("DROP TRIGGER trg_planning_sequence_guard_arming");
        await scene.ExecuteSqlAsync("DROP TRIGGER trg_planning_sequence_guard_tuple");
        await using (var context = fixture.CreateContext())
        {
            context.PlanningSequences.Add(PlanningSequence.Arm(
                Guid.NewGuid(), scene.RunId, scene.ProjectId, scene.WorkspaceId, scene.CheckpointId, scene.FingerprintSha256, new string('b', 64),
                LocalExecutionSequenceTestScene.Start));
            await context.SaveChangesAsync();
        }

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.PlanningActiveCode);
    }

    [Fact]
    public async Task A_workspace_owner_the_project_blocker_does_not_see_refuses_with_workspace_owned()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        // A running Agent attempt of a run of ANOTHER project that names this workspace: no project can attest the association.
        await using (var context = fixture.CreateContext())
        {
            var foreign = await Projects.WorkspaceExecutionSeed.SeedAsync(context);
            var attempt = Attempt.ClaimAgentImplementation(
                Guid.NewGuid(), foreign.Run.Id, 1, scene.WorkspaceId, scene.CheckpointId, LocalExecutionSequenceTestScene.StartFingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, LocalExecutionSequenceTestScene.Start, 1);
            attempt.MarkAgentDispatched(LocalExecutionSequenceTestScene.Start);
            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();
        }

        var availability = await scene.ReadAvailabilityAsync(plan.ProposalId);

        Assert.False(availability.Available);
        Assert.Contains(
            availability.RefusalCode,
            new[] { LocalExecutionSequenceErrors.WorkspaceOwnedCode, LocalExecutionSequenceErrors.ProjectBusyCode });
        Assert.Null(await scene.FindSequenceAsync());
    }

    [Fact]
    public async Task Two_owners_recorded_elsewhere_at_limit_two_refuse_with_capacity_unavailable()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 2);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Verification);

        await AssertRefusedAsync(scene, plan.ProposalId, LocalExecutionSequenceErrors.CapacityUnavailableCode);
    }

    [Fact]
    public async Task Two_recipes_sharing_a_number_are_an_incoherent_set()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 2);
        var plan = await scene.AddAcceptedRootPlanAsync();
        await scene.ExecuteSqlAsync("DROP INDEX IF EXISTS ix_verification_commands_ProjectId_CommandNumber");
        await scene.ExecuteSqlAsync("UPDATE verification_commands SET CommandNumber = 1 WHERE ProjectId = {0}", scene.ProjectId);

        var availability = await scene.ReadAvailabilityAsync(plan.ProposalId);

        Assert.False(availability.Available);
        Assert.Equal(LocalExecutionSequenceErrors.RecipesIncoherentCode, availability.RefusalCode);
    }
}
