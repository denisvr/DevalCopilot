using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Commands.UpdateVerificationCommand;
using DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.SetClaudeMutationTurnLimit;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// Arming against every reciprocal manual writer on independent SQLite connections (ADR-0060): either order is decided by the write
/// lock and the database guards, never both outcomes, and never a partial write. The Human arming and the manual writer each hold
/// their own context.
/// </summary>
public sealed class LocalExecutionSequenceArmingRaceTests
{
    private const int Rounds = 3;

    public static IEnumerable<object[]> Writers() => new[] { "implementation", "verification", "abandon", "recipe_edit", "setting_edit" }
        .Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task Arming_and_a_manual_writer_racing_are_decided_by_the_locks_with_no_partial_write(string writer)
    {
        for (var round = 0; round < Rounds; round++)
        {
            await using var fixture = new SqliteDatabaseFixture();
            await fixture.InitializeAsync();
            var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 2);
            var plan = await scene.AddAcceptedRootPlanAsync();
            var command = await scene.ArmCommandAsync(plan.ProposalId);
            var recipe = (await scene.ReadRecipesAsync())[0];

            var arming = Task.Run(() => scene.TryArmAsync(command));
            var manual = Task.Run(() => RunWriterAsync(fixture, scene, plan, recipe, writer));
            await Task.WhenAll(arming, manual);

            var armed = await arming;
            var manualSucceeded = await manual;
            var sequence = await scene.FindSequenceAsync();
            // Exactly one side won: the sequence exists only when arming succeeded and no manual write got in first.
            Assert.Equal(armed.IsSuccess, sequence is not null);
            Assert.False(armed.IsSuccess && manualSucceeded, $"Both {writer} and the arming succeeded.");
            if (sequence is null)
            {
                Assert.Empty(await scene.ReadPinnedRecipesAsync());
                Assert.Equal(0, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceArmed));
            }
            else
            {
                Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceArmed));
                // The pinned whole set is the stored set: no recipe or run setting changed underneath it.
                var pins = await scene.ReadPinnedRecipesAsync();
                var current = await scene.ReadRecipesAsync();
                Assert.Equal(current.Count, pins.Count);
                Assert.All(pins.Zip(current), pair => Assert.True(pair.First.Matches(pair.Second)));
                Assert.Equal(
                    LocalExecutionSequenceTestScene.StartFingerprint, (await scene.ReadLatestCheckpointAsync()).FingerprintSha256);
                var run = await scene.ReadRunAsync();
                Assert.Equal(RunLifecycle.Running, run.Lifecycle);
                Assert.Equal(sequence.SettingsSnapshot, Application.Features.Runs.Policies.LocalExecutionSequences.LocalExecutionSettingsSnapshot.Of(run));
                Assert.DoesNotContain(await scene.ReadLocalAttemptsAsync(), attempt => attempt.Status == AttemptStatus.Running);
                Assert.Empty(await scene.ReadExecutionsAsync());
            }
        }
    }

    private static async Task<bool> RunWriterAsync(
        SqliteDatabaseFixture fixture,
        LocalExecutionSequenceTestScene scene,
        LocalExecutionSequenceTestScene.SeededPlan plan,
        Domain.Features.Projects.VerificationCommand recipe,
        string writer)
    {
        try
        {
            switch (writer)
            {
                case "implementation":
                    {
                        await using var context = fixture.CreateContext();
                        return (await new CreateImplementationAttemptCommandHandler(
                            context, scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe).HandleAsync(
                            new CreateImplementationAttemptCommand(scene.RunId, plan.ProposalId), CancellationToken.None)).IsSuccess;
                    }

                case "verification":
                    {
                        await using var context = fixture.CreateContext();
                        return (await new ClaimVerificationExecutionCommandHandler(context, scene.Evidence, scene.Clock).HandleAsync(
                            new ClaimVerificationExecutionCommand(scene.ProjectId, recipe.Id, scene.CheckpointId), CancellationToken.None)).IsSuccess;
                    }

                case "abandon":
                    {
                        await using var context = fixture.CreateContext();
                        return (await new AbandonManualRunCommandHandler(context, scene.Clock).HandleAsync(
                            new AbandonManualRunCommand(scene.RunId, "No longer needed."), CancellationToken.None)).IsSuccess;
                    }

                case "recipe_edit":
                    return (await scene.PipelineAsync(context => new UpdateVerificationCommandCommandHandler(context, scene.Clock).HandleAsync(
                        new UpdateVerificationCommandCommand(
                            scene.ProjectId, recipe.Id, recipe.Name, recipe.ExecutablePath, recipe.Arguments, recipe.TimeoutSeconds + 1, true),
                        CancellationToken.None))).IsSuccess;

                default:
                    {
                        await using var context = fixture.CreateContext();
                        return (await new SetClaudeMutationTurnLimitCommandHandler(context, scene.Clock).HandleAsync(
                            new SetClaudeMutationTurnLimitCommand(scene.RunId, 5), CancellationToken.None)).IsSuccess;
                    }
            }
        }
        catch (DbUpdateException)
        {
            // The database guard refused the save of the loser: nothing was written.
            return false;
        }
    }
}
