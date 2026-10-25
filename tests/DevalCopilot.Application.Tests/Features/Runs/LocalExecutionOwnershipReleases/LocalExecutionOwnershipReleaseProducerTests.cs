using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;

/// <summary>
/// ADR-0065 provenance producers: the dispatch witness commits atomically with the existing single-use dispatch marker of an exact
/// linked operation (never otherwise), pins its complete tuple and the original host launch, and the opaque ticket is returned only
/// with a committed marker. A refused, failed or ambiguous commit yields no usable ticket and invokes nothing, and nothing ever
/// reconstructs a ticket for a restarted host.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseProducerTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId)> ArmedAsync(int recipes = 2)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipes);
        var plan = await scene.AddAcceptedRootPlanAsync();
        return (scene, (await scene.ArmAsync(plan.ProposalId)).SequenceId);
    }

    [Theory]
    [MemberData(nameof(LocalExecutionOwnershipReleaseCycleTests.Matrix), MemberType = typeof(LocalExecutionOwnershipReleaseCycleTests))]
    public async Task Every_dispatched_operation_of_a_cycle_has_one_witness_pinning_its_exact_tuple_and_original_host(Step target, bool queued)
    {
        var ended = await LocalExecutionOwnershipReleaseRig.EndAsync(fixture, target, queued);
        var scene = ended.Scene;
        var steps = (await scene.ReadStepsAsync()).Where(step => step.OperationId is not null).ToList();
        var witnesses = await scene.ReadWitnessesAsync();
        var admissions = await scene.ReadAllAdmissionsAsync();

        Assert.Equal(steps.Count, witnesses.Count);
        Assert.All(steps, step =>
        {
            var witness = witnesses.Single(candidate => candidate.StepId == step.Id);
            Assert.Equal(ended.Sequence.Id, witness.SequenceId);
            Assert.Equal(scene.RunId, witness.RunId);
            Assert.Equal(scene.ProjectId, witness.ProjectId);
            Assert.Equal(scene.WorkspaceId, witness.WorkspaceId);
            Assert.Equal(step.Kind, witness.Kind);
            Assert.Equal(step.Ordinal, witness.Ordinal);
            Assert.Equal(step.PassNumber, witness.PassNumber);
            Assert.Equal(step.RecipeOrdinal, witness.RecipeOrdinal);
            Assert.Equal(step.PredecessorStepId, witness.PredecessorStepId);
            Assert.Equal(step.SourceCheckpointId, witness.SourceCheckpointId);
            Assert.Equal(step.SourceFingerprintSha256, witness.SourceFingerprintSha256);
            Assert.Equal(step.ReportMessageId, witness.ReportMessageId);
            Assert.Equal(step.SubjectAttemptId, witness.SubjectAttemptId);
            Assert.Equal(step.OperationId, witness.OperationId);
            Assert.Equal(scene.HostLaunch.Id, witness.HostLaunchId);
            Assert.NotEqual(Guid.Empty, witness.Nonce);
            Assert.Equal(
                queued ? admissions.Single(admission => admission.StepId == step.Id).Id : null,
                witness.AdmissionId);
            if (step.Kind == Step.Verification)
            {
                Assert.Equal(LocalExecutionOperationFamily.Verification, witness.OperationFamily);
                Assert.Equal(step.OperationId, witness.VerificationExecutionId);
                Assert.Null(witness.AgentAttemptId);
                Assert.Null(witness.AgentResponseContract);
            }
            else
            {
                Assert.Equal(LocalExecutionOperationFamily.Agent, witness.OperationFamily);
                Assert.Equal(step.OperationId, witness.AgentAttemptId);
                Assert.Null(witness.VerificationExecutionId);
                Assert.Equal(LocalExecutionSequencePolicy.ContractOf(step.Kind), witness.AgentResponseContract);
            }
        });

        // Distinct invocations hold distinct private nonces.
        Assert.Equal(witnesses.Count, witnesses.Select(witness => witness.Nonce).Distinct().Count());
    }

    [Fact]
    public async Task A_refused_dispatch_commits_no_witness_and_returns_no_ticket()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var claimed = await scene.ReserveAndClaimAsync(sequenceId);
        scene.Clock.Advance(TimeSpan.FromHours(3));

        var dispatched = await scene.DispatchAgentAsync(claimed.ReservedOperationId);

        Assert.True(dispatched.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.LocalExecutionSequenceDispatchRefusedCode, dispatched.Errors[0].Code);
        Assert.Empty(await scene.ReadWitnessesAsync());
        Assert.Null(scene.TicketOf(claimed.ReservedOperationId));
        Assert.Null((await scene.ReadAttemptAsync(claimed.ReservedOperationId)).AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task A_dispatch_commit_that_fails_leaves_the_operation_undispatched_without_a_witness_or_a_ticket()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var claimed = await scene.ReserveAndClaimAsync(sequenceId);

        var thrown = await CommitWithFaultAsync(scene, claimed.ReservedOperationId, FaultInjectingDbContext.CommitFailureMode.BeforeCommit);

        Assert.IsType<FaultInjectingDbContext.SimulatedDbException>(thrown);
        Assert.Empty(await scene.ReadWitnessesAsync());
        Assert.Null((await scene.ReadAttemptAsync(claimed.ReservedOperationId)).AgentDispatchedAtUtc);
        Assert.Null(scene.TicketOf(claimed.ReservedOperationId));
    }

    [Fact]
    public async Task An_ambiguous_dispatch_commit_leaves_a_witness_but_no_ticket_so_its_quarantine_can_never_be_released()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var claimed = await scene.ReserveAndClaimAsync(sequenceId);

        // The marker and witness became durable, but the caller only ever saw a failure: it was handed no ticket.
        var thrown = await CommitWithFaultAsync(scene, claimed.ReservedOperationId, FaultInjectingDbContext.CommitFailureMode.AfterCommit);

        Assert.IsType<FaultInjectingDbContext.SimulatedDbException>(thrown);
        Assert.NotNull((await scene.ReadAttemptAsync(claimed.ReservedOperationId)).AgentDispatchedAtUtc);
        Assert.Single(await scene.ReadWitnessesAsync());
        Assert.Null(scene.TicketOf(claimed.ReservedOperationId));

        // The host records the process result it did observe, but cannot show the original ticket: ordinary recording is preserved and
        // no observation exists, so the quarantine stays unsupported forever.
        await scene.RecordImplementationAsync(claimed.ReservedOperationId, ProviderTimedOutTerminated);
        await scene.ObserveAsync(sequenceId);

        Assert.Empty(await scene.ReadObservationsAsync());
        var step = await scene.StepOfAsync(claimed.ReservedOperationId);
        var read = await scene.ReadReleaseAsync(step.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Unavailable, read.Status);
        Assert.Equal("evidence_missing", read.ReasonCode);
        var refused = await scene.ReleaseAsync(new Application.Features.Runs.Commands.ReleaseLocalExecutionOwnership.ReleaseLocalExecutionOwnershipCommand(
            scene.RunId, step.Id, Guid.NewGuid(), 1, new string('0', 64), scene.Clock.GetUtcNow().AddMinutes(1)));
        Assert.Equal("local_execution_ownership_release.unavailable", refused.Errors[0].Code);
        Assert.Empty(await scene.ReadReleasesAsync());
    }

    [Fact]
    public async Task A_restarted_host_holds_no_ticket_and_cannot_add_an_observation_for_an_earlier_dispatch()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var claimed = await scene.ReserveAndClaimAsync(sequenceId);
        var dispatched = await scene.DispatchAgentAsync(claimed.ReservedOperationId);
        var originalTicket = dispatched.Value.Ticket;
        Assert.NotNull(originalTicket);

        scene.RestartHost();
        Assert.Null(scene.TicketOf(claimed.ReservedOperationId));
        Assert.NotEqual(Guid.Empty, scene.HostLaunch.Id);

        // The restarted host has the result but not the ticket: the result records ordinarily with no observation.
        await scene.RecordImplementationAsync(claimed.ReservedOperationId, ProviderTimedOutTerminated);

        Assert.Empty(await scene.ReadObservationsAsync());
        var witness = Assert.Single(await scene.ReadWitnessesAsync());
        Assert.NotEqual(scene.HostLaunch.Id, witness.HostLaunchId);
    }

    private async Task<Exception?> CommitWithFaultAsync(
        LocalExecutionSequenceTestScene scene, Guid attemptId, FaultInjectingDbContext.CommitFailureMode mode)
    {
        await using var read = fixture.CreateContext();
        var fact = PlanningImplementationAuthorizationAttempt.ToFact(
            await PlanningImplementationAuthorizationAttempt.ClassifyFreshAsync(read, scene.RunId, attemptId, CancellationToken.None));
        await using var real = fixture.CreateContext();
        var db = new FaultInjectingDbContext(real) { CommitFailure = mode };
        await using var transaction = await db.BeginTransactionAsync(CancellationToken.None);
        var result = await new MarkAgentAttemptDispatchedCommandHandler(db, scene.Clock, scene.HostLaunch).HandleAsync(
            new MarkAgentAttemptDispatchedCommand(scene.RunId, attemptId, null, fact), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        await db.SaveChangesAsync(CancellationToken.None);
        return await Record.ExceptionAsync(() => transaction.CommitAsync());
    }
}
