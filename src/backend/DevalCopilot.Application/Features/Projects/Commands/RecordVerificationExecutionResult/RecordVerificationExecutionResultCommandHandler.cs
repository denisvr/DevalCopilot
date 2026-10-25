using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionResult;

public sealed class RecordVerificationExecutionResultCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordVerificationExecutionResultCommand, Result<VerificationExecutionStatus>>
{
    public async Task<Result<VerificationExecutionStatus>> HandleAsync(
        RecordVerificationExecutionResultCommand command, CancellationToken cancellationToken)
    {
        var execution = await dbContext.VerificationExecutions.SingleOrDefaultAsync(
            candidate => candidate.Id == command.VerificationExecutionId, cancellationToken);
        if (execution is null)
        {
            return Result<VerificationExecutionStatus>.Failure(
                Error.NotFound("verification.execution_not_found", "This verification execution was not found."));
        }

        if (execution.Status != VerificationExecutionStatus.Running || !execution.DispatchedAtUtc.HasValue)
        {
            return Result<VerificationExecutionStatus>.Failure(
                Error.Conflict("verification.execution_not_running", "This verification execution cannot record a result."));
        }

        // ADR-0065: the physical termination observation of a linked verification, decided under this result transaction from the original
        // host's ticket and stored atomically with the ordinary result. Missing optional evidence preserves ordinary recording; supplied
        // evidence that is foreign to, or contradicts, the original dispatch refuses the whole transaction.
        var termination = await LocalExecutionTerminationRecording.DecideExecutionAsync(
            dbContext,
            command.DispatchTicket,
            execution,
            command.Outcome,
            command.ExitCode,
            command.ProcessDuration,
            command.Termination,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (termination.Refusal is { } terminationRefusal)
        {
            return Result<VerificationExecutionStatus>.Failure(terminationRefusal);
        }

        execution.Complete(command.Outcome, command.ExitCode, command.CompletionFingerprintSha256, timeProvider.GetUtcNow());
        if (termination.Observation is { } observation)
        {
            dbContext.LocalExecutionTerminationObservations.Add(observation);
        }

        foreach (var sealedArtifact in command.SealedArtifacts)
        {
            dbContext.VerificationOutputArtifacts.Add(VerificationOutputArtifact.Record(
                Guid.NewGuid(),
                execution.Id,
                sealedArtifact.Purpose,
                sealedArtifact.RelativeStoragePath,
                sealedArtifact.ContentHash,
                sealedArtifact.ByteLength,
                sealedArtifact.Truncated,
                VerificationOutputCaptureOutcome.CapturedWithKnownTruncation,
                timeProvider.GetUtcNow()));
        }
        return Result<VerificationExecutionStatus>.Success(execution.Status);
    }
}
