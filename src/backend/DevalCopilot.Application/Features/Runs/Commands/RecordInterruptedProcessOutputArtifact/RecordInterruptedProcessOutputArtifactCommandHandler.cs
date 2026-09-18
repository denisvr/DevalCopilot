using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordInterruptedProcessOutputArtifact;

public sealed class RecordInterruptedProcessOutputArtifactCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordInterruptedProcessOutputArtifactCommand, Result<bool>>
{
    public async Task<Result<bool>> HandleAsync(
        RecordInterruptedProcessOutputArtifactCommand command, CancellationToken cancellationToken)
    {
        if (command.Purpose is not (ArtifactPurpose.ProcessStandardOutput or ArtifactPurpose.ProcessStandardError))
        {
            return Result<bool>.Failure(
                Error.Conflict("artifacts.unsupported_purpose", "Only process stdout/stderr recovery is supported."));
        }

        var attempt = await dbContext.Attempts
            .SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (attempt is null)
        {
            return Result<bool>.Failure(
                Error.NotFound("attempts.not_found", "The requested attempt was not found."));
        }

        if (attempt.RunId != command.RunId)
        {
            return Result<bool>.Failure(
                Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Kind != AttemptKind.Process)
        {
            return Result<bool>.Failure(
                Error.Conflict("attempts.not_process", "The attempt is not a Process attempt."));
        }

        // The only state this recovery path is ever valid for: the attempt is still Running,
        // exactly as GetRunningProcessAttemptsQuery found it — recovery always runs before
        // reconciliation flips it to Interrupted. An attempt in any other state (already
        // reconciled, already completed normally, or never dispatched) is not this path's to
        // touch.
        if (attempt.Status != AttemptStatus.Running)
        {
            return Result<bool>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status}, not eligible for interrupted-output recovery."));
        }

        // The primary idempotency guard: a prior recovery pass (or a normal completion that
        // raced an unlikely restart) may already have recorded this exact (attempt, purpose).
        // The database's unique index on the same pair is the backstop, not the primary check.
        var alreadyRecorded = await dbContext.Artifacts
            .AnyAsync(a => a.AttemptId == command.AttemptId && a.Purpose == command.Purpose, cancellationToken);

        if (alreadyRecorded)
        {
            return Result<bool>.Success(false);
        }

        var nowUtc = timeProvider.GetUtcNow();
        var artifact = Artifact.Record(
            Guid.NewGuid(),
            command.RunId,
            command.AttemptId,
            command.Purpose,
            "text/plain; charset=utf-8",
            command.RelativeStoragePath,
            command.ContentHash,
            command.ByteLength,
            // Genuinely unknown: whether the interrupted host session's partial capture had
            // already been truncated before it stopped writing was never observed, so this is
            // never reported as a known `false`.
            truncated: null,
            ArtifactCaptureOutcome.PartialHostInterrupted,
            ArtifactSensitivity.RedactedBestEffort,
            ArtifactRetentionPolicy.RetainUntilRunDeleted,
            nowUtc);
        dbContext.Artifacts.Add(artifact);

        var payload = JsonSerializer.Serialize(new
        {
            artifactId = artifact.Id,
            purpose = command.Purpose.ToString(),
            byteLength = command.ByteLength,
            truncated = (bool?)null,
            captureOutcome = ArtifactCaptureOutcome.PartialHostInterrupted.ToString(),
            sensitivity = artifact.Sensitivity.ToString(),
            retentionPolicy = artifact.RetentionPolicy.ToString(),
        });
        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(), command.RunId, command.AttemptId, RunEventType.ProcessOutputCaptured,
            ParticipantKind.Orchestrator, payload, nowUtc));

        return Result<bool>.Success(true);
    }
}
