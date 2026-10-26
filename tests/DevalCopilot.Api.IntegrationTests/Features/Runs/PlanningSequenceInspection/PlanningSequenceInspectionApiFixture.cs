using DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequenceInspection;

/// <summary>
/// One REAL recorded planning sequence for the read-only inspection tests (ADR-0068): the production arming, the real coordinator and
/// the three real supervisors with their provider doubles drive the whole five-step path (Planner, a challenging review, a Resolver, a
/// second challenging review and a second Resolver) to the Human escalation. The hosts are then stopped, so every inspection read below
/// is a pure read of durable rows through the production API host. A test class that mutates storage owns its own fixture; the shared
/// one is never written.
/// </summary>
public sealed class PlanningSequenceInspectionApiFixture : IAsyncLifetime
{
    internal PlanningSequenceHostedHarness Harness { get; private set; } = null!;

    internal PlanningSequenceHostedHarness.Scene Scene { get; private set; } = null!;

    internal PlanningSequenceInspectionHost Host { get; private set; } = null!;

    internal PlanningSequence Sequence { get; private set; } = null!;

    internal IReadOnlyList<PlanningSequenceStep> Steps { get; private set; } = [];

    internal IReadOnlyList<Attempt> Attempts { get; private set; } = [];

    public async Task InitializeAsync()
    {
        Harness = new PlanningSequenceHostedHarness();
        Harness.Reviewer.Decisions.Enqueue(1);
        Harness.Reviewer.Decisions.Enqueue(2);
        Scene = await Harness.SeedSceneAsync();
        var armed = await Harness.ArmAsync(Scene);
        await Harness.StartHostsAsync();
        Sequence = await Harness.WaitForEndAsync(armed.SequenceId);
        await Harness.StopHostsAsync();
        await using (var context = Harness.OpenContext())
        {
            Steps = await context.PlanningSequenceSteps.AsNoTracking().Where(step => step.SequenceId == Sequence.Id)
                .OrderBy(step => step.Ordinal).ToListAsync();
            Attempts = (await context.Attempts.AsNoTracking().Where(attempt => attempt.RunId == Scene.RunId).ToListAsync())
                .OrderBy(attempt => attempt.AttemptNumber).ToList();
        }

        Host = new PlanningSequenceInspectionHost(Harness.DatabasePath, Harness.ArtifactStore);
    }

    public async Task DisposeAsync()
    {
        Host.Dispose();
        await Harness.DisposeAsync();
    }
}
