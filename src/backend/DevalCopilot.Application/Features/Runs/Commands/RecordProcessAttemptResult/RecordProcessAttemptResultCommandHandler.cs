using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordProcessAttemptResult;

public sealed class RecordProcessAttemptResultCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordProcessAttemptResultCommand, Result<AttemptStatus>>
{
    public async Task<Result<AttemptStatus>> HandleAsync(
        RecordProcessAttemptResultCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs
            .SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        var attempt = await dbContext.Attempts
            .SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (run is null || attempt is null)
        {
            return Result<AttemptStatus>.Failure(Error.NotFound("runs.not_found", "The requested run or attempt was not found."));
        }

        if (attempt.RunId != run.Id)
        {
            return Result<AttemptStatus>.Failure(
                Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Kind != AttemptKind.Process)
        {
            return Result<AttemptStatus>.Failure(
                Error.Conflict("attempts.not_process", "The attempt is not a Process attempt."));
        }

        if (attempt.Status != AttemptStatus.Running || run.Lifecycle != RunLifecycle.Running)
        {
            return Result<AttemptStatus>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status} and cannot record a result."));
        }

        var nowUtc = timeProvider.GetUtcNow();

        if (command.Outcome is { } outcome)
        {
            attempt.CompleteProcess(outcome, command.ExitCode, nowUtc);
        }
        else
        {
            attempt.Fail(nowUtc);
        }

        if (attempt.Status == AttemptStatus.Completed)
        {
            run.Complete(nowUtc);
        }
        else
        {
            run.Fail(nowUtc);
        }

        if (command.SealedArtifacts.Count > 0)
        {
            foreach (var sealedArtifact in command.SealedArtifacts)
            {
                RecordArtifact(command.RunId, command.AttemptId, sealedArtifact, ArtifactCaptureOutcome.Captured, nowUtc);
            }
        }
        else
        {
            // Nothing was ever sealed for this attempt — the adapter failed before a child
            // process or output sink existed, so there was never anything to capture. Without
            // this, the terminal Attempt/Run rows would be truthfully Failed while the event
            // journal stayed silent about why. Exactly one event, metadata-only: never the
            // exception, arguments, environment, or any output detail.
            dbContext.Events.Add(RunEvent.Record(
                Guid.NewGuid(),
                command.RunId,
                command.AttemptId,
                RunEventType.ProcessEndedWithoutOutput,
                ParticipantKind.Orchestrator,
                JsonSerializer.Serialize(new
                {
                    summary = "Process attempt ended without any captured output.",
                    status = attempt.Status.ToString(),
                }),
                nowUtc));
        }

        return Result<AttemptStatus>.Success(attempt.Status);
    }

    private void RecordArtifact(
        Guid runId, Guid attemptId, SealedOutputArtifact sealedArtifact, ArtifactCaptureOutcome captureOutcome, DateTimeOffset nowUtc)
    {
        var artifact = Artifact.Record(
            Guid.NewGuid(),
            runId,
            attemptId,
            sealedArtifact.Purpose,
            "text/plain; charset=utf-8",
            sealedArtifact.RelativeStoragePath,
            sealedArtifact.ContentHash,
            sealedArtifact.ByteLength,
            sealedArtifact.Truncated,
            captureOutcome,
            ArtifactSensitivity.RedactedBestEffort,
            ArtifactRetentionPolicy.RetainUntilRunDeleted,
            nowUtc);
        dbContext.Artifacts.Add(artifact);

        var payload = JsonSerializer.Serialize(new
        {
            artifactId = artifact.Id,
            purpose = sealedArtifact.Purpose.ToString(),
            byteLength = sealedArtifact.ByteLength,
            truncated = sealedArtifact.Truncated,
            captureOutcome = captureOutcome.ToString(),
            sensitivity = artifact.Sensitivity.ToString(),
            retentionPolicy = artifact.RetentionPolicy.ToString(),
        });
        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(), runId, attemptId, RunEventType.ProcessOutputCaptured, ParticipantKind.Orchestrator, payload, nowUtc));
    }
}
