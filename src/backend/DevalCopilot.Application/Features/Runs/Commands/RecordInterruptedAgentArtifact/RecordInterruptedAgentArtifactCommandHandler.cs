using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordInterruptedAgentArtifact;

public sealed class RecordInterruptedAgentArtifactCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordInterruptedAgentArtifactCommand, Result<bool>>
{
    public async Task<Result<bool>> HandleAsync(RecordInterruptedAgentArtifactCommand command, CancellationToken cancellationToken)
    {
        if (command.Purpose is not (ArtifactPurpose.AgentStandardOutput or ArtifactPurpose.AgentStandardError
            or ArtifactPurpose.AgentFinalResponse))
        {
            return Result<bool>.Failure(
                Error.Conflict("artifacts.unsupported_purpose", "Only Agent output/final-response recovery is supported."));
        }

        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (attempt is null || attempt.RunId != command.RunId)
        {
            return Result<bool>.Failure(Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Kind != AttemptKind.Agent)
        {
            return Result<bool>.Failure(Error.Conflict("attempts.not_agent", "The attempt is not an Agent attempt."));
        }

        if (attempt.Status != AttemptStatus.Running)
        {
            return Result<bool>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status}, not eligible for interrupted-output recovery."));
        }

        var alreadyRecorded = await dbContext.Artifacts
            .AnyAsync(a => a.AttemptId == command.AttemptId && a.Purpose == command.Purpose, cancellationToken);
        if (alreadyRecorded)
        {
            return Result<bool>.Success(false);
        }

        var nowUtc = timeProvider.GetUtcNow();
        var mediaType = command.Purpose == ArtifactPurpose.AgentFinalResponse ? "application/json" : "text/plain; charset=utf-8";
        var artifact = Artifact.Record(
            Guid.NewGuid(),
            command.RunId,
            command.AttemptId,
            command.Purpose,
            mediaType,
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

        // Distinct from AgentAttemptCompleted: the owning attempt is still Running when this is
        // recorded — recovering one artifact is never itself a terminal result.
        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(),
            command.RunId,
            command.AttemptId,
            RunEventType.AgentOutputRecovered,
            ParticipantIdentity.ForOrchestrator(),
            JsonSerializer.Serialize(new
            {
                artifactId = artifact.Id,
                purpose = command.Purpose.ToString(),
                byteLength = command.ByteLength,
                captureOutcome = ArtifactCaptureOutcome.PartialHostInterrupted.ToString(),
            }),
            nowUtc));

        return Result<bool>.Success(true);
    }
}
