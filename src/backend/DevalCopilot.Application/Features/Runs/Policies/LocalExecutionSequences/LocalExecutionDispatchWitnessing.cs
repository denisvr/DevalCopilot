using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// ADR-0065: records the dispatch witness of one exact linked local execution operation inside the same commit as its existing single-use
/// dispatch marker, and returns the opaque ticket the original host keeps in memory. Both marker handlers call it only after
/// <see cref="LocalExecutionDispatchGate"/> judged the exact linked step dispatchable and every source, account, workspace, capacity and
/// deadline gate passed; it never replaces or weakens one. The association must be complete: a queued cycle's step must have exactly one
/// Consumed admission carrying the step's tuple (never a pre-claim reservation), and a UUID equal in another family proves nothing. An
/// association that cannot be proved returns null, which the handler maps to the ordinary dispatch refusal, so nothing is invoked. The
/// ticket is created here and only here; nothing reconstructs one after a restart.
/// </summary>
internal static class LocalExecutionDispatchWitnessing
{
    public static async Task<LocalExecutionDispatchTicket?> RecordAgentAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionHostLaunch hostLaunch,
        LocalExecutionDispatchGate.Evaluation evaluation,
        Attempt attempt,
        DateTimeOffset dispatchedAtUtc,
        CancellationToken cancellationToken)
    {
        if (evaluation.Sequence is not { } sequence || evaluation.Step is not { } step || attempt.AgentResponseContract is not { } contract
            || await ReadAdmissionAsync(dbContext, sequence, step, cancellationToken) is not { } admission)
        {
            return null;
        }

        try
        {
            var witness = LocalExecutionDispatchWitness.RecordAgent(
                Guid.NewGuid(), sequence, step, attempt.Id, contract, admission.Id, dispatchedAtUtc, hostLaunch.Id, Guid.NewGuid());
            return Add(dbContext, witness);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static async Task<LocalExecutionDispatchTicket?> RecordVerificationAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionHostLaunch hostLaunch,
        LocalExecutionDispatchGate.Evaluation evaluation,
        VerificationExecution execution,
        DateTimeOffset dispatchedAtUtc,
        CancellationToken cancellationToken)
    {
        if (evaluation.Sequence is not { } sequence || evaluation.Step is not { } step
            || await ReadAdmissionAsync(dbContext, sequence, step, cancellationToken) is not { } admission)
        {
            return null;
        }

        try
        {
            var witness = LocalExecutionDispatchWitness.RecordVerification(
                Guid.NewGuid(), sequence, step, execution.Id, execution.VerificationCommandId, admission.Id, dispatchedAtUtc, hostLaunch.Id,
                Guid.NewGuid());
            return Add(dbContext, witness);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The consumed queued admission of the step, or an "absent" marker for an immediate cycle. An incomplete, duplicate or
    /// non-consumed queued admission is not provable and yields null.</summary>
    private static async Task<AdmissionReading?> ReadAdmissionAsync(
        IDevalCopilotDbContext dbContext, LocalExecutionSequence sequence, LocalExecutionSequenceStep step, CancellationToken cancellationToken)
    {
        if (sequence.AdmissionMode == LocalExecutionAdmissionMode.Immediate)
        {
            return await dbContext.LocalExecutionAdmissions.AsNoTracking().AnyAsync(
                candidate => candidate.SequenceId == sequence.Id && candidate.StepId == step.Id, cancellationToken)
                ? null
                : new AdmissionReading(null);
        }

        var admissions = await dbContext.LocalExecutionAdmissions.AsNoTracking()
            .Where(candidate => candidate.StepId == step.Id)
            .Take(2)
            .ToListAsync(cancellationToken);
        return admissions is [{ } admission]
            && admission.SequenceId == sequence.Id
            && admission.State == LocalExecutionAdmissionState.Consumed
            && admission.Ordinal == step.Ordinal
            && admission.Kind == step.Kind
            && admission.PassNumber == step.PassNumber
            && admission.RecipeOrdinal == step.RecipeOrdinal
            && admission.SourceCheckpointId == step.SourceCheckpointId
            && string.Equals(admission.SourceFingerprintSha256, step.SourceFingerprintSha256, StringComparison.Ordinal)
            ? new AdmissionReading(admission.Id)
            : null;
    }

    private static LocalExecutionDispatchTicket Add(IDevalCopilotDbContext dbContext, LocalExecutionDispatchWitness witness)
    {
        dbContext.LocalExecutionDispatchWitnesses.Add(witness);
        return new LocalExecutionDispatchTicket(
            witness.Id,
            witness.OperationFamily,
            witness.OperationId,
            witness.StepId,
            witness.SequenceId,
            witness.HostLaunchId,
            witness.Nonce,
            witness.DispatchedAtUtc);
    }

    private sealed record AdmissionReading(Guid? Id);
}
