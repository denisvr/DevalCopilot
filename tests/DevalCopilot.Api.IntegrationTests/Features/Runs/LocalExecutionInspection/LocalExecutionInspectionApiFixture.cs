using DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionInspection;

/// <summary>
/// One REAL recorded local execution cycle for the read-only inspection tests (ADR-0061): the production-written lineage, the real
/// coordinator and the real supervisors with their process doubles drive an implementation, a first verification pass whose first recipe
/// fails with sealed output, one failure diagnosis, its correction and a second all-passing pass to an approved review. The hosts are then
/// stopped, so every inspection read below is a pure read of durable rows and real sealed files through the production API host. A test
/// class that mutates storage owns its own fixture; the shared one is never written.
/// </summary>
public sealed class LocalExecutionInspectionApiFixture : IAsyncLifetime
{
    internal LocalExecutionSequenceHostedHarness Harness { get; private set; } = null!;

    internal LocalExecutionSequenceLineage Lineage { get; private set; } = null!;

    internal LocalExecutionInspectionHost Host { get; private set; } = null!;

    internal LocalExecutionSequence Sequence { get; private set; } = null!;

    internal IReadOnlyList<LocalExecutionSequenceStep> Steps { get; private set; } = [];

    public int Recipes { get; init; } = 2;

    public bool FailFirstRecipe { get; init; } = true;

    public async Task InitializeAsync()
    {
        Harness = new LocalExecutionSequenceHostedHarness();
        Lineage = await Harness.SeedLineageAsync(recipeCount: Recipes);
        if (FailFirstRecipe)
        {
            Harness.Process.Verification = call => call is { RecipeNumber: 1, RecipeCall: 1 }
                ? LocalExecutionSequenceVerificationBehavior.Fails
                : LocalExecutionSequenceVerificationBehavior.Passes;
            Harness.Diagnosis.Answers.Enqueue(LocalExecutionSequenceResponses.DiagnosisFindings(2));
        }

        var armed = await Harness.ArmAsync(Lineage);
        await Harness.StartHostsAsync();
        Sequence = await Harness.WaitForEndAsync(armed.SequenceId);
        await Harness.StopHostsAsync();
        Steps = await Harness.StepsAsync(Sequence.Id);
        Host = new LocalExecutionInspectionHost(Harness.DatabasePath, Harness.ArtifactStore);
    }

    public async Task DisposeAsync()
    {
        Host.Dispose();
        await Harness.DisposeAsync();
    }
}
