using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Commands.ArmLocalExecutionSequence;
using DevalCopilot.Application.Features.Runs.Commands.ClaimLocalExecutionSequenceStep;
using DevalCopilot.Application.Features.Runs.Commands.ObserveLocalExecutionQueue;
using DevalCopilot.Application.Features.Runs.Commands.ReserveLocalExecutionAdmission;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceAvailability;
using DevalCopilot.Application.Tests.Features.Projects;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The queue (ADR-0062) additions of the scene. Intake and the host's two queue commands are the real production handlers over a context
/// of their own, so the races between a Stop, a reservation, a claim and a manual writer are real SQLite races. The reservation and the
/// page observation are global (one database), exactly as the host calls them.
/// </summary>
internal sealed partial class LocalExecutionSequenceTestScene
{
    // ---- intake -----------------------------------------------------------------------------------------------------------------

    public async Task<GetLocalExecutionSequenceAvailabilityQueryResult> ReadQueueAvailabilityAsync(Guid planProposalMessageId)
    {
        await using var context = Fixture.CreateContext();
        var result = await new GetLocalExecutionSequenceAvailabilityQueryHandler(context).HandleAsync(
            new GetLocalExecutionSequenceAvailabilityQuery(RunId, planProposalMessageId, LocalExecutionAdmissionMode.WaitForAdmission),
            CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    public ArmLocalExecutionSequenceCommand QueueCommandFrom(GetLocalExecutionSequenceAvailabilityQueryResult availability) =>
        CommandFrom(availability) with { Mode = LocalExecutionAdmissionMode.WaitForAdmission };

    public async Task<ArmLocalExecutionSequenceCommand> QueueCommandAsync(Guid planProposalMessageId)
    {
        var availability = await ReadQueueAvailabilityAsync(planProposalMessageId);
        Assert.True(availability.Available, availability.RefusalCode);
        return QueueCommandFrom(availability);
    }

    /// <summary>Queues the plan with the confirmation of a queue availability read taken now, asserting success.</summary>
    public async Task<ArmLocalExecutionSequenceCommandResult> QueueAsync(Guid planProposalMessageId)
    {
        var result = await TryArmAsync(await QueueCommandAsync(planProposalMessageId));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        Assert.Equal(LocalExecutionAdmissionMode.WaitForAdmission, result.Value.AdmissionMode);
        return result.Value;
    }

    // ---- the host's two queue commands ------------------------------------------------------------------------------------------

    public async Task<ObserveLocalExecutionQueueCommandResult> ObserveQueueAsync(
        long after = 0, long? highWater = null, bool workerAvailable = true)
    {
        await using var context = Fixture.CreateContext();
        var result = await new ObserveLocalExecutionQueueCommandHandler(context, Clock).HandleAsync(
            new ObserveLocalExecutionQueueCommand(after, highWater, workerAvailable), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    /// <summary>Observes the whole queue: every page of one finite sweep.</summary>
    public async Task<int> ObserveWholeQueueAsync(bool workerAvailable = true)
    {
        long after = 0;
        long? high = null;
        var visited = 0;
        for (var page = 0; page < 8; page++)
        {
            var observed = await ObserveQueueAsync(after, high, workerAvailable);
            visited += observed.Visited;
            if (observed.SweepFinished)
            {
                break;
            }

            after = observed.LastCoordinationNumber;
            high = observed.HighWaterCoordinationNumber;
        }

        return visited;
    }

    /// <summary>The host's reservation of the oldest eligible admission; null when none is eligible.</summary>
    public async Task<LocalExecutionStepTicket?> ReserveAsync()
    {
        await using var context = Fixture.CreateContext();
        var result = await new ReserveLocalExecutionAdmissionCommandHandler(context, Clock).HandleAsync(
            new ReserveLocalExecutionAdmissionCommand(), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value.ReservedStep;
    }

    /// <summary>The tail of a coordinator tick for an already reserved queued step: its one claim, then (when it committed) the
    /// dispatch marker and the scripted result. A refused claim is settled by the real resolve command.</summary>
    public async Task<DrivenStep> DriveTicketAsync(LocalExecutionStepTicket ticket, LocalExecutionStepScript? script = null)
    {
        script ??= new LocalExecutionStepScript();
        var claim = await ClaimAsync(ticket);
        if (claim.IsFailure)
        {
            await ResolveAsync(ticket, claim.Errors[0].Code);
            return new DrivenStep(ticket, claim, null, (await ReadSequenceAsync()).State);
        }

        var step = new LocalExecutionStepContext(ticket.Kind, 0, null, ticket.Ordinal, ticket.ReservedOperationId, script.Findings);
        var row = (await ReadStepsAsync()).Single(candidate => candidate.Id == ticket.StepId);
        step = step with { PassNumber = row.PassNumber, RecipeOrdinal = row.RecipeOrdinal };
        var kind = script.Decide(step);
        if (kind != ScriptedResultKind.LeaveUndispatched)
        {
            await DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
            if (kind != ScriptedResultKind.LeaveRunning)
            {
                await RecordAsync(step, kind);
            }
        }

        return new DrivenStep(ticket, claim, kind, (await ReadSequenceAsync()).State);
    }

    // ---- reads ------------------------------------------------------------------------------------------------------------------

    public async Task<List<LocalExecutionAdmission>> ReadAdmissionsAsync()
    {
        await using var context = Fixture.CreateContext();
        return await context.LocalExecutionAdmissions.AsNoTracking()
            .Where(admission => context.LocalExecutionSequences.Any(sequence => sequence.Id == admission.SequenceId && sequence.RunId == RunId))
            .OrderBy(admission => admission.AdmissionNumber).ToListAsync();
    }

    public async Task<List<LocalExecutionAdmission>> ReadAllAdmissionsAsync()
    {
        await using var context = Fixture.CreateContext();
        return await context.LocalExecutionAdmissions.AsNoTracking().OrderBy(admission => admission.AdmissionNumber).ToListAsync();
    }

    /// <summary>Another run of the same project (so the same owned workspace), ready for its own plan and queue; its fake Git is its own.</summary>
    public async Task<LocalExecutionSequenceTestScene> SiblingRunAsync()
    {
        await using var context = Fixture.CreateContext();
        var project = await context.Projects.SingleAsync(candidate => candidate.Id == ProjectId);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Implement a sibling increment", Start);
        run.Claim(Start);
        context.Runs.Add(run);
        await context.SaveChangesAsync(CancellationToken.None);
        return new LocalExecutionSequenceTestScene(Fixture, run.Id, ProjectId, WorkspaceId, CheckpointId, WorkspacePath);
    }

    /// <summary>Places a coherent owner of <paramref name="family"/> on THIS scene's workspace (or on the workspace of an owner already
    /// placed there), through its own connection, exactly as another operator action would.</summary>
    public async Task<PlacedOwner> AddWorkspaceOwnerAsync(OwnerFamily family, PlacedOwner? onto = null) =>
        await ExecutionCapacitySeed.AddOntoAsync(
            Fixture, onto ?? new PlacedOwner(OwnerFamily.Agent, Guid.Empty, ProjectId, RunId, WorkspaceId, CheckpointId), family);

    /// <summary>Gives the project a NEWER Ready owned workspace and an unresolved owner on the OLDER one: the project is blocked while the
    /// latest workspace itself is idle.</summary>
    public async Task<(Guid NewerWorkspaceId, Guid OlderOwnerId)> AddNewerIdleWorkspaceWithOlderOwnerAsync()
    {
        await using var db = Fixture.CreateContext();
        var run = await WorkspaceExecutionSeed.AddRunAsync(db, ProjectId, 7000 + Interlocked.Increment(ref olderOwnerRuns));
        var owner = await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, run.Id, WorkspaceId, CheckpointId);
        var newer = GitWorkspace.Prepare(Guid.NewGuid(), ProjectId, 2, $@"C:\workspaces\{Guid.NewGuid():N}", "branch-2", Head, "main", Start);
        newer.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), newer.Id, newer.ReserveCheckpointNumber(), Start, Head, new string('e', 64), []);
        var lease = RepositoryMutationLease.Acquire(
            Guid.NewGuid(), ProjectId, newer.Id, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0), Guid.NewGuid().ToByteArray(), Start);
        db.GitWorkspaces.Add(newer);
        db.GitCheckpoints.Add(checkpoint);
        db.RepositoryMutationLeases.Add(lease);
        await db.SaveChangesAsync(CancellationToken.None);
        return (newer.Id, owner);
    }

    private static int olderOwnerRuns;

    /// <summary>Releases the workspace's active repository lease (a pinned authority fact the queue revalidates), through its own context.</summary>
    public async Task ReleaseLeaseAsync()
    {
        await using var context = Fixture.CreateContext();
        var lease = await context.RepositoryMutationLeases.SingleAsync(candidate => candidate.WorkspaceId == WorkspaceId && candidate.Status == LeaseStatus.Active);
        lease.Release(Clock.GetUtcNow());
        await context.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Records an operation row under the exact reserved identity of an unclaimed reservation: a contradictory shape (a Reserved,
    /// unlinked step whose operation already exists) that no honest claim produces and that retirement must therefore refuse to prove.</summary>
    public async Task PlantReservedOperationAsync(LocalExecutionStepTicket ticket)
    {
        await using var context = Fixture.CreateContext();
        context.Attempts.Add(Attempt.ClaimAgentImplementation(
            ticket.ReservedOperationId, RunId, 900, WorkspaceId, CheckpointId, StartFingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10),
            1024, 2048, Start, 900));
        await context.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>The workspace ownership reading of the scene's project exactly as every entrance sees it.</summary>
    public async Task<DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions.WorkspaceExecutionAssessment> AssessWorkspaceAsync(
        LocalExecutionStepTicket? ownReservation = null)
    {
        await using var context = Fixture.CreateContext();
        return await DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions.WorkspaceExecutionPolicy.AssessAsync(
            context, ProjectId, WorkspaceId, null, null, CancellationToken.None, ownReservation);
    }

    /// <summary>The global recorded mutating owner reading exactly as every mutating entrance sees it.</summary>
    public async Task<DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity.ExecutionCapacityReading> ReadCapacityAsync(
        LocalExecutionStepTicket? ownReservation = null)
    {
        await using var context = Fixture.CreateContext();
        return await DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity.ExecutionCapacityPolicy.ReadAsync(
            context, null, CancellationToken.None, ownReservation);
    }
}
