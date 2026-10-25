using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.SetExecutionCapacityLimit;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Xunit;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;

/// <summary>
/// ADR-0065: a release changes only the ownership of the one released operation. Every entrance that the quarantine withheld (a manual
/// claim, a queued reservation, the global limit, abandonment and the manual-control fence) is admitted or refused by its OWN fresh gate
/// afterwards; the release itself reserves, claims, dispatches and starts nothing, and it grants the work that passes no new authority.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseEntranceTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Theory]
    [InlineData(Step.Implementation)]
    [InlineData(Step.CodeReview)]
    [InlineData(Step.Verification)]
    public async Task A_manual_claim_is_refused_while_the_quarantine_holds_and_passes_its_own_gates_after_the_release(Step kind)
    {
        var ended = await LocalExecutionOwnershipReleaseRig.EndAsync(fixture, kind, queued: false);
        var sibling = await ended.Scene.SiblingRunAsync();
        var plan = await sibling.AddAcceptedRootPlanAsync();

        var refused = await sibling.Mediator.SendAsync(new CreateImplementationAttemptCommand(sibling.RunId, plan.ProposalId));
        Assert.True(refused.IsFailure);
        Assert.Contains(refused.Errors[0].Code, new[] { WorkspaceExecutionErrors.AgentAttemptActiveCode, WorkspaceExecutionErrors.VerificationActiveCode });
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, (await ended.Scene.ReadStatusAsync()).ManualFence);

        await ended.Scene.ReleaseStepAsync(ended.Target.Id);

        Assert.Equal(LocalExecutionSequenceManualFence.Released, (await ended.Scene.ReadStatusAsync()).ManualFence);
        var afterwards = await sibling.Mediator.SendAsync(new CreateImplementationAttemptCommand(sibling.RunId, plan.ProposalId));
        if (kind == Step.Implementation)
        {
            Assert.True(afterwards.IsSuccess, afterwards.IsFailure ? afterwards.Errors[0].Code : null);
            return;
        }

        // The release never repairs source or workspace gates: the earlier steps moved the workspace on, so this plan's own checkpoint
        // gate still refuses, and no longer for any ownership reason.
        Assert.True(afterwards.IsFailure);
        Assert.Equal("agent_attempts.checkpoint_not_current", afterwards.Errors[0].Code);
    }

    [Fact]
    public async Task Already_queued_work_waits_while_the_quarantine_holds_and_is_reserved_by_its_own_gates_only_after_the_release()
    {
        var ended = await LocalExecutionOwnershipReleaseRig.EndAsync(fixture, Step.Implementation, queued: false);
        var sibling = await ended.Scene.SiblingRunAsync();
        var plan = await sibling.AddAcceptedRootPlanAsync();
        await sibling.QueueAsync(plan.ProposalId);
        await sibling.ObserveWholeQueueAsync();

        Assert.Null(await sibling.ReserveAsync());
        Assert.Equal(LocalExecutionWaitingReason.WaitingForWorkspace, Assert.Single(await sibling.ReadAdmissionsAsync()).WaitingReason);

        await ended.Scene.ReleaseStepAsync(ended.Target.Id);

        // The release started nothing: the queued admission still waits, with no reservation, claim or attempt created by it.
        var untouched = Assert.Single(await sibling.ReadAdmissionsAsync());
        Assert.Equal(LocalExecutionAdmissionState.Waiting, untouched.State);
        Assert.Empty(await sibling.ReadLocalAttemptsAsync());

        var ticket = await sibling.ReserveAsync();
        Assert.NotNull(ticket);
        Assert.True((await sibling.ClaimAsync(ticket!)).IsSuccess);
        Assert.Equal(LocalExecutionAdmissionState.Consumed, Assert.Single(await sibling.ReadAdmissionsAsync()).State);
    }

    [Fact]
    public async Task Global_capacity_admits_queued_mutating_work_only_after_the_release_and_never_beyond_its_limit()
    {
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        var ended = await LocalExecutionOwnershipReleaseRig.EndAsync(fixture, Step.Implementation, queued: false);

        // Another PROJECT with its own idle workspace: only the one recorded mutating owner of the held operation can withhold it.
        var other = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);
        var otherPlan = await other.AddAcceptedRootPlanAsync();
        await other.QueueAsync(otherPlan.ProposalId);
        await other.ObserveWholeQueueAsync();
        Assert.Null(await other.ReserveAsync());
        Assert.Equal(LocalExecutionWaitingReason.WaitingForCapacity, Assert.Single(await other.ReadAdmissionsAsync()).WaitingReason);
        Assert.Equal(1, (await other.ReadCapacityAsync()).RecordedOwners);

        await ended.Scene.ReleaseStepAsync(ended.Target.Id);

        Assert.Equal(0, (await other.ReadCapacityAsync()).RecordedOwners);
        var ticket = await other.ReserveAsync();
        Assert.NotNull(ticket);
        Assert.True((await other.ClaimAsync(ticket!)).IsSuccess);
        Assert.Equal(1, (await other.ReadCapacityAsync()).RecordedOwners);
    }

    [Fact]
    public async Task A_lower_global_limit_is_refused_while_the_quarantined_owner_counts_and_accepted_once_it_is_released()
    {
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 2);
        var ended = await LocalExecutionOwnershipReleaseRig.EndAsync(fixture, Step.Implementation, queued: false);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        Assert.Equal(2, (await ended.Scene.ReadCapacityAsync()).RecordedOwners);

        var refused = await LowerLimitAsync(ended.Scene);
        Assert.True(refused.IsFailure);

        await ended.Scene.ReleaseStepAsync(ended.Target.Id);

        Assert.Equal(1, (await ended.Scene.ReadCapacityAsync()).RecordedOwners);
        var accepted = await LowerLimitAsync(ended.Scene);
        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Errors[0].Code : null);
    }

    [Fact]
    public async Task A_release_that_commits_first_and_one_that_arrives_after_a_reservation_both_leave_exactly_one_owner_and_one_receipt()
    {
        var ended = await LocalExecutionOwnershipReleaseRig.EndAsync(fixture, Step.Implementation, queued: false);
        var sibling = await ended.Scene.SiblingRunAsync();
        var plan = await sibling.AddAcceptedRootPlanAsync();
        await sibling.QueueAsync(plan.ProposalId);
        await sibling.ObserveWholeQueueAsync();
        var command = ended.Scene.ConfirmedCommand(await ended.Scene.ReadReleaseAsync(ended.Target.Id));

        // Order one: the reservation is attempted first and is withheld by the still-held quarantine.
        Assert.Null(await sibling.ReserveAsync());
        // Order two: the release then commits and the same reservation is admitted by a fresh read.
        Assert.True((await ended.Scene.ReleaseAsync(command)).IsSuccess);
        var ticket = await sibling.ReserveAsync();
        Assert.NotNull(ticket);

        // The release is independent of the later reservation: an identical request still replays, and the new owner is counted once.
        Assert.True((await ended.Scene.ReleaseAsync(command)).IsSuccess);
        Assert.Single(await ended.Scene.ReadReleasesAsync());
        Assert.Equal(WorkspaceExecutionStatus.PreparationReserved, (await ended.Scene.AssessWorkspaceAsync()).Status);
        Assert.Equal(1, (await ended.Scene.ReadCapacityAsync()).RecordedOwners);
    }

    [Fact]
    public async Task Concurrent_identical_releases_and_reservations_never_produce_two_receipts_or_two_owners()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var isolated = new SqliteDatabaseFixture();
            await isolated.InitializeAsync();
            var ended = await LocalExecutionOwnershipReleaseRig.EndAsync(isolated, Step.Implementation, queued: false);
            var sibling = await ended.Scene.SiblingRunAsync();
            var plan = await sibling.AddAcceptedRootPlanAsync();
            await sibling.QueueAsync(plan.ProposalId);
            await sibling.ObserveWholeQueueAsync();
            var command = ended.Scene.ConfirmedCommand(await ended.Scene.ReadReleaseAsync(ended.Target.Id));

            var releases = Enumerable.Range(0, 3).Select(_ => Task.Run(() => ended.Scene.ReleaseAsync(command))).ToArray();
            var reservation = Task.Run(() => sibling.ReserveAsync());
            await Task.WhenAll(releases);
            var reserved = await reservation;

            Assert.All(releases, release => Assert.True(release.Result.IsSuccess, release.Result.IsFailure ? release.Result.Errors[0].Code : null));
            Assert.Single(await ended.Scene.ReadReleasesAsync());
            Assert.Equal(1, await ended.Scene.CountEventsAsync(RunEventType.LocalExecutionOwnershipReleased));
            Assert.Single(releases.Select(release => release.Result.Value.ReleaseId).Distinct());

            // Whatever the interleaving, the reservation was either withheld by the held quarantine or admitted after the release.
            var ownership = await LocalExecutionOwnershipReleaseRig.ReadOwnershipAsync(isolated, ended.Scene);
            Assert.Equal(reserved is null ? WorkspaceExecutionStatus.Idle : WorkspaceExecutionStatus.PreparationReserved, ownership.Workspace);
            Assert.True(await ended.Scene.CountEventsAsync(RunEventType.LocalExecutionOwnershipReleased) == 1);
        }
    }

    private static async Task<Devalente.Shared.Results.Result<SetExecutionCapacityLimitCommandResult>> LowerLimitAsync(LocalExecutionSequenceTestScene scene)
    {
        await using var context = scene.Fixture.CreateContext();
        return await new SetExecutionCapacityLimitCommandHandler(context).HandleAsync(
            new SetExecutionCapacityLimitCommand(1), CancellationToken.None);
    }
}
