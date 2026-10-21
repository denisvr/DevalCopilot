using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;
using DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionSourceChanged;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The verification dispatch gate compares the execution's own arguments with the Human-confirmed pinned recipe (ADR-0060): any drift
/// between the claim and the dispatch marker (values, order, additions, removals, emptying) refuses the marker dedicatedly, after which the
/// dedicated refusal recorder closes the proved-undispatched claim with no process or adapter call. The ordinary feed-versus-marker snapshot
/// equality stays independent, and a pre-dispatch SourceChanged still composes for a linked execution.
/// </summary>
public sealed class LocalExecutionVerificationArgumentDispatchTests : IAsyncLifetime
{
    public static IEnumerable<object[]> Drifts() => new[] { "changed", "reordered", "appended", "removed", "empty" }.Select(name => new object[] { name });

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, LocalExecutionStepTicket Ticket)> ClaimedVerificationAsync()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var sequenceId = (await scene.ArmAsync(plan.ProposalId)).SequenceId;
        var ticket = await scene.ReserveUntilAsync(sequenceId, LocalExecutionStepKind.Verification);
        Assert.True((await scene.ClaimAsync(ticket)).IsSuccess);
        return (scene, ticket);
    }

    private async Task SetArgumentsAsync(Guid executionId, string[] arguments)
    {
        await using var context = fixture.CreateContext();
        var execution = await context.VerificationExecutions.SingleAsync(row => row.Id == executionId);
        var property = context.Entry(execution).Property(row => row.Arguments);
        property.CurrentValue = arguments;
        property.IsModified = true;
        await context.SaveChangesAsync();
    }

    [Theory]
    [MemberData(nameof(Drifts))]
    public async Task Argument_drift_before_the_dispatch_marker_is_refused_and_the_refusal_is_then_recordable_without_any_process(string drift)
    {
        var (scene, ticket) = await ClaimedVerificationAsync();
        var pinned = (await scene.ReadExecutionAsync(ticket.ReservedOperationId)).Arguments.ToArray();
        Assert.Equal(["--check", "1"], pinned);
        await SetArgumentsAsync(ticket.ReservedOperationId, drift switch
        {
            "changed" => ["--check", "2"],
            "reordered" => ["1", "--check"],
            "appended" => [.. pinned, "--extra"],
            "removed" => [pinned[0]],
            _ => [],
        });
        var captures = scene.Evidence.Captures;

        var dispatched = await scene.DispatchVerificationAsync(ticket.ReservedOperationId);

        Assert.True(dispatched.IsFailure);
        Assert.Equal(MarkVerificationExecutionDispatchedCommandHandler.LocalExecutionSequenceDispatchRefusedCode, dispatched.Errors[0].Code);
        var untouched = await scene.ReadExecutionAsync(ticket.ReservedOperationId);
        Assert.Null(untouched.DispatchedAtUtc);
        Assert.Equal(VerificationExecutionStatus.Running, untouched.Status);

        Assert.True((await scene.RecordVerificationDispatchRefusalAsync(ticket.ReservedOperationId)).IsSuccess);
        var refused = await scene.ReadExecutionAsync(ticket.ReservedOperationId);
        Assert.Equal(VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused, refused.Status);
        Assert.Null(refused.Outcome);
        Assert.Null(refused.ExitCode);
        Assert.Null(refused.DispatchedAtUtc);
        Assert.Equal(captures, scene.Evidence.Captures);
    }

    [Fact]
    public async Task An_exact_match_of_the_pinned_arguments_dispatches()
    {
        var (scene, ticket) = await ClaimedVerificationAsync();

        var dispatched = await scene.DispatchVerificationAsync(ticket.ReservedOperationId);

        Assert.True(dispatched.IsSuccess, dispatched.IsFailure ? dispatched.Errors[0].Code : null);
        Assert.NotNull((await scene.ReadExecutionAsync(ticket.ReservedOperationId)).DispatchedAtUtc);
    }

    [Fact]
    public async Task The_feed_snapshot_equality_stays_independent_for_an_unlinked_execution()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        await scene.AddAcceptedRootPlanAsync();
        await using (var context = fixture.CreateContext())
        {
            var recipe = (await scene.ReadRecipesAsync())[0];
            Assert.True((await new ClaimVerificationExecutionCommandHandler(context, scene.Evidence, scene.Clock)
                .HandleAsync(new(scene.ProjectId, recipe.Id, scene.CheckpointId), CancellationToken.None)).IsSuccess);
        }

        var execution = (await scene.ReadExecutionsAsync()).Single();
        var snapshot = VerificationDispatchSnapshot.Of(execution);
        await using var marker = fixture.CreateContext();

        var refused = await new MarkVerificationExecutionDispatchedCommandHandler(marker, scene.Clock).HandleAsync(
            new MarkVerificationExecutionDispatchedCommand(execution.Id, snapshot with { Arguments = ["--other"] }), CancellationToken.None);

        Assert.True(refused.IsFailure);
        Assert.Equal("verification.execution_snapshot_changed", refused.Errors[0].Code);
        Assert.Null((await scene.ReadExecutionAsync(execution.Id)).DispatchedAtUtc);
        var allowed = await scene.DispatchVerificationAsync(execution.Id);
        Assert.True(allowed.IsSuccess);
    }

    [Fact]
    public async Task A_pre_dispatch_source_change_is_still_recorded_for_a_linked_execution_whose_git_moved()
    {
        var (scene, ticket) = await ClaimedVerificationAsync();
        var execution = await scene.ReadExecutionAsync(ticket.ReservedOperationId);
        await using var context = fixture.CreateContext();

        var recorded = await new RecordVerificationExecutionSourceChangedCommandHandler(context, scene.Clock).HandleAsync(
            new RecordVerificationExecutionSourceChangedCommand(execution.Id, new string('e', 64), VerificationDispatchSnapshot.Of(execution)),
            CancellationToken.None);

        Assert.True(recorded.IsSuccess, recorded.IsFailure ? recorded.Errors[0].Code : null);
        var after = await scene.ReadExecutionAsync(execution.Id);
        Assert.Equal(VerificationExecutionStatus.SourceChanged, after.Status);
        Assert.Equal(new string('e', 64), after.CompletionFingerprintSha256);
        Assert.Null(after.DispatchedAtUtc);
    }
}
