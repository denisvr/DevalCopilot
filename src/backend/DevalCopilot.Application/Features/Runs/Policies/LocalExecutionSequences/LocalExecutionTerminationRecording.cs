using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// ADR-0065: the one decision the five ordinary result recorders (four Agent contracts and verification) make about the physical
/// termination observation of their own operation. It is decided under the recorder's own result transaction and the observation is then
/// stored atomically with the ordinary result, never by a separate command and never later.
///
/// <list type="bullet">
/// <item><description>No ticket, or no optional termination evidence in the returned process result: nothing to record and the ordinary
/// recording is preserved exactly. A nonlocal operation persists no new fact.</description></item>
/// <item><description>A ticket with termination evidence must belong to the one dispatch witness of this exact operation: the same
/// witness, host launch, private nonce and dispatch time, the operation still the exact claimed step of that sequence with the witness's
/// full tuple, no observation recorded yet, and a physical fact that does not contradict the returned process result. Anything else
/// refuses the whole transaction; a foreign or contradictory observation is never downgraded into trustworthy absence.</description></item>
/// </list>
/// The observation's recording time is the host's own time and never an exact process-exit timestamp.
/// </summary>
internal static class LocalExecutionTerminationRecording
{
    internal sealed record Decision(Error? Refusal, LocalExecutionTerminationObservation? Observation)
    {
        public static Decision Nothing { get; } = new(null, null);

        public static Decision Refused { get; } = new(LocalExecutionOwnershipReleaseErrors.ObservationRefused(), null);

        public static Decision Record(LocalExecutionTerminationObservation observation) => new(null, observation);
    }

    public static async Task<Decision> DecideAgentAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionDispatchTicket? ticket,
        Attempt attempt,
        AgentProcessEvidence? evidence,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (ticket is null || evidence?.Termination is not { } termination)
        {
            return Decision.Nothing;
        }

        if (ticket.Family != LocalExecutionOperationFamily.Agent || ticket.OperationId != attempt.Id
            || attempt.AgentDispatchedAtUtc is not { } dispatchedAtUtc)
        {
            return Decision.Refused;
        }

        ProcessOutcome? outcome = evidence.Outcome switch
        {
            ProcessExecutionOutcome.Exited => ProcessOutcome.Exited,
            ProcessExecutionOutcome.TimedOut => ProcessOutcome.TimedOut,
            ProcessExecutionOutcome.Cancelled => ProcessOutcome.Cancelled,
            _ => null,
        };
        return outcome is { } known
            ? await DecideAsync(
                dbContext, ticket, dispatchedAtUtc, known, evidence.ExitCode, evidence.Duration, termination, nowUtc, cancellationToken)
            : Decision.Refused;
    }

    public static async Task<Decision> DecideExecutionAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionDispatchTicket? ticket,
        VerificationExecution execution,
        VerificationExecutionOutcome outcome,
        int? exitCode,
        TimeSpan? processDuration,
        ProcessTerminationObservation? termination,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (ticket is null || termination is null)
        {
            return Decision.Nothing;
        }

        ProcessOutcome? mapped = outcome switch
        {
            VerificationExecutionOutcome.Exited => ProcessOutcome.Exited,
            VerificationExecutionOutcome.TimedOut => ProcessOutcome.TimedOut,
            VerificationExecutionOutcome.Cancelled => ProcessOutcome.Cancelled,
            _ => null,
        };
        if (ticket.Family != LocalExecutionOperationFamily.Verification || ticket.OperationId != execution.Id
            || execution.DispatchedAtUtc is not { } dispatchedAtUtc || mapped is not { } known || processDuration is not { } duration)
        {
            return Decision.Refused;
        }

        return await DecideAsync(dbContext, ticket, dispatchedAtUtc, known, exitCode, duration, termination, nowUtc, cancellationToken);
    }

    private static async Task<Decision> DecideAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionDispatchTicket ticket,
        DateTimeOffset operationDispatchedAtUtc,
        ProcessOutcome outcome,
        int? semanticExitCode,
        TimeSpan processDuration,
        ProcessTerminationObservation termination,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        // The witness is read afresh and untracked: nothing the context tracked earlier is authority for the original dispatch.
        var witness = await dbContext.LocalExecutionDispatchWitnesses.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == ticket.WitnessId, cancellationToken);
        if (witness is null
            || witness.OperationFamily != ticket.Family
            || witness.OperationId != ticket.OperationId
            || witness.StepId != ticket.StepId
            || witness.SequenceId != ticket.SequenceId
            || witness.HostLaunchId != ticket.HostLaunchId
            || witness.Nonce != ticket.Nonce
            || witness.DispatchedAtUtc != ticket.DispatchedAtUtc
            || witness.DispatchedAtUtc != operationDispatchedAtUtc)
        {
            return Decision.Refused;
        }

        var step = await dbContext.LocalExecutionSequenceSteps.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == witness.StepId, cancellationToken);
        if (step is null
            || step.SequenceId != witness.SequenceId
            || step.OperationId != witness.OperationId
            || step.State != LocalExecutionSequenceStepState.Claimed
            || step.Kind != witness.Kind
            || step.Ordinal != witness.Ordinal
            || step.PassNumber != witness.PassNumber
            || step.RecipeOrdinal != witness.RecipeOrdinal
            || step.PredecessorStepId != witness.PredecessorStepId
            || step.SourceCheckpointId != witness.SourceCheckpointId
            || !string.Equals(step.SourceFingerprintSha256, witness.SourceFingerprintSha256, StringComparison.Ordinal)
            || step.ReportMessageId != witness.ReportMessageId
            || step.SubjectAttemptId != witness.SubjectAttemptId)
        {
            return Decision.Refused;
        }

        if (await dbContext.LocalExecutionTerminationObservations.AsNoTracking()
                .AnyAsync(candidate => candidate.WitnessId == witness.Id, cancellationToken)
            || !LocalExecutionTerminationObservation.IsCoherent(
                outcome, semanticExitCode, processDuration, termination.PhysicalExitCode, termination.ObservedElapsed))
        {
            return Decision.Refused;
        }

        try
        {
            return Decision.Record(LocalExecutionTerminationObservation.Record(
                Guid.NewGuid(), witness, outcome, semanticExitCode, processDuration, termination.PhysicalExitCode, termination.ObservedElapsed, nowUtc));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            return Decision.Refused;
        }
    }
}
