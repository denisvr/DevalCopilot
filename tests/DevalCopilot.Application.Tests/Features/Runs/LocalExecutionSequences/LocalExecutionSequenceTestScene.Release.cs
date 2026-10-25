using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using DevalCopilot.Application.Features.Runs.Commands.ReleaseLocalExecutionOwnership;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// ADR-0065 test support: the one host launch and the in-memory dispatch tickets the scene's original host holds, the three append-only
/// facts and the production release query and command handlers. A "restart" is a new launch that holds no earlier ticket, exactly like
/// a restarted host: nothing here reconstructs a ticket.
/// </summary>
internal sealed partial class LocalExecutionSequenceTestScene
{
    private readonly Dictionary<Guid, LocalExecutionDispatchTicket> tickets = [];

    /// <summary>The host launch every dispatch marker of this scene commits (replaced by <see cref="RestartHost"/>).</summary>
    public LocalExecutionHostLaunch HostLaunch { get; private set; } = new();

    /// <summary>A restarted host: a different launch identity that holds none of the earlier tickets.</summary>
    public void RestartHost()
    {
        HostLaunch = new LocalExecutionHostLaunch();
        tickets.Clear();
    }

    private void RememberTicket(Guid operationId, LocalExecutionDispatchTicket? ticket)
    {
        if (ticket is not null)
        {
            tickets[operationId] = ticket;
        }
    }

    /// <summary>The ticket the original host received from the committed marker of the operation, or null.</summary>
    public LocalExecutionDispatchTicket? TicketOf(Guid operationId) => tickets.GetValueOrDefault(operationId);

    public async Task<List<LocalExecutionDispatchWitness>> ReadWitnessesAsync()
    {
        await using var context = Fixture.CreateContext();
        return await context.LocalExecutionDispatchWitnesses.AsNoTracking().OrderBy(witness => witness.Ordinal).ToListAsync();
    }

    public async Task<List<LocalExecutionTerminationObservation>> ReadObservationsAsync()
    {
        await using var context = Fixture.CreateContext();
        return await context.LocalExecutionTerminationObservations.AsNoTracking().ToListAsync();
    }

    public async Task<List<LocalExecutionOwnershipRelease>> ReadReleasesAsync()
    {
        await using var context = Fixture.CreateContext();
        return await context.LocalExecutionOwnershipReleases.AsNoTracking().ToListAsync();
    }

    public async Task<LocalExecutionSequenceStep> StepOfAsync(Guid operationId) =>
        (await ReadStepsAsync()).Single(step => step.OperationId == operationId);

    public async Task<LocalExecutionSequenceStep> LastLinkedStepAsync() =>
        (await ReadStepsAsync()).Where(step => step.OperationId is not null).OrderBy(step => step.Ordinal).Last();

    public async Task<Result<GetLocalExecutionOwnershipReleaseQueryResult>> TryReadReleaseAsync(Guid runId, Guid stepId)
    {
        await using var context = Fixture.CreateContext();
        return await new GetLocalExecutionOwnershipReleaseQueryHandler(context, Clock).HandleAsync(
            new GetLocalExecutionOwnershipReleaseQuery(runId, stepId), CancellationToken.None);
    }

    public async Task<GetLocalExecutionOwnershipReleaseQueryResult> ReadReleaseAsync(Guid stepId)
    {
        var result = await TryReadReleaseAsync(RunId, stepId);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    /// <summary>The exact command a Human submits after seeing the Available candidate.</summary>
    public ReleaseLocalExecutionOwnershipCommand ConfirmedCommand(GetLocalExecutionOwnershipReleaseQueryResult read)
    {
        Assert.True(read.Status == LocalExecutionOwnershipReleaseStatus.Available, $"{read.Status}/{read.ReasonCode}");
        var candidate = read.Candidate!;
        return new ReleaseLocalExecutionOwnershipCommand(
            read.RunId, read.StepId, candidate.Facts.ObservationId, candidate.ConfirmationVersion, candidate.ConfirmationSha256,
            candidate.ConfirmationExpiresAtUtc);
    }

    public async Task<Result<ReleaseLocalExecutionOwnershipCommandResult>> ReleaseAsync(
        ReleaseLocalExecutionOwnershipCommand command, IDevalCopilotDbContext? db = null, IRunEventNotifier? notifier = null)
    {
        if (db is not null)
        {
            return await new ReleaseLocalExecutionOwnershipCommandHandler(db, Clock, notifier).HandleAsync(command, CancellationToken.None);
        }

        await using var context = Fixture.CreateContext();
        return await new ReleaseLocalExecutionOwnershipCommandHandler(context, Clock, notifier).HandleAsync(command, CancellationToken.None);
    }

    /// <summary>Reads the step's candidate and releases it with the exact confirmation, asserting success.</summary>
    public async Task<ReleaseLocalExecutionOwnershipCommandResult> ReleaseStepAsync(Guid stepId)
    {
        var released = await ReleaseAsync(ConfirmedCommand(await ReadReleaseAsync(stepId)));
        Assert.True(released.IsSuccess, released.IsFailure ? released.Errors[0].Code : null);
        return released.Value;
    }

    /// <summary>The production implementation result recorder with an explicit process evidence and ticket, returning its result so a
    /// refusal can be asserted. The source stays unchanged, so a non-success records ProviderInvocationFailed.</summary>
    public async Task<Result<RecordImplementationResultCommandResult>> TryRecordImplementationAsync(
        Guid attemptId, AgentProcessEvidence? evidence, LocalExecutionDispatchTicket? ticket, bool succeeded = false)
    {
        var attempt = await ReadAttemptAsync(attemptId);
        var command = new RecordImplementationResultCommand(
            RunId, attemptId, succeeded, Head, attempt.AgentCheckpointFingerprintSha256, [], [], null, null, null, null, evidence)
        {
            DispatchTicket = ticket,
        };
        return await PipelineAsync(context => new RecordImplementationResultCommandHandler(context, Clock).HandleAsync(command, CancellationToken.None));
    }

    /// <summary>The production verification result recorder with an explicit outcome, termination fact and ticket.</summary>
    public async Task<Result<VerificationExecutionStatus>> TryRecordVerificationAsync(
        Guid executionId,
        VerificationExecutionOutcome outcome,
        int? exitCode,
        ProcessTerminationObservation? termination,
        TimeSpan? processDuration,
        LocalExecutionDispatchTicket? ticket)
    {
        var execution = await ReadExecutionAsync(executionId);
        return await PipelineAsync(context => new RecordVerificationExecutionResultCommandHandler(context, Clock).HandleAsync(
            new RecordVerificationExecutionResultCommand(executionId, outcome, exitCode, execution.CheckpointFingerprintSha256, [])
            {
                DispatchTicket = ticket,
                Termination = termination,
                ProcessDuration = processDuration,
            },
            CancellationToken.None));
    }
}
