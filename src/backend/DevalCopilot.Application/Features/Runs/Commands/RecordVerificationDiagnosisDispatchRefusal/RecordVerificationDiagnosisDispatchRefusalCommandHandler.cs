using System.Text.Json;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Domain.Features.Runs;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisDispatchRefusal;

public sealed class RecordVerificationDiagnosisDispatchRefusalCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordVerificationDiagnosisDispatchRefusalCommand, Result>
{
    public async Task<Result> HandleAsync(RecordVerificationDiagnosisDispatchRefusalCommand command, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);
        if (attempt is null || attempt.RunId != command.RunId)
        {
            return Result.Failure(Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Kind != AttemptKind.Agent || attempt.AgentResponseContract != AgentResponseContract.VerificationDiagnosis)
        {
            return Result.Failure(Error.Conflict("attempts.not_verification_diagnosis", "The attempt is not a verification-diagnosis attempt."));
        }

        if (attempt.Status != AttemptStatus.Running || attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result.Failure(Error.Conflict(
                "attempts.not_eligible", "Only an undispatched, running diagnosis attempt can record a dispatch refusal."));
        }

        AgentOutcome outcome;
        switch (command.Refusal)
        {
            case VerificationDiagnosisDispatchRefusal.InputAlreadyDiagnosed:
                {
                    var reportId = await VerificationDiagnosisInputIdentity.ReadPinnedExecutionReportIdAsync(dbContext, attempt.Id, cancellationToken);
                    var pinned = await VerificationDiagnosisInputIdentity.ReadPinnedPairsAsync(dbContext, attempt.Id, cancellationToken);
                    if (reportId is null
                        || !await VerificationDiagnosisInputIdentity.HasCompetingSuccessfulDiagnosisAsync(
                            dbContext, attempt.RunId, attempt.Id, reportId.Value, pinned.Select(pair => pair.ExecutionId).ToArray(), cancellationToken))
                    {
                        return Result.Failure(Error.Conflict(
                            "attempts.not_eligible", "No competing successful diagnosis of this exact input identity exists."));
                    }

                    outcome = AgentOutcome.InputAlreadyDiagnosed;
                    break;
                }

            case VerificationDiagnosisDispatchRefusal.VerificationEvidenceChanged:
                {
                    if (await VerificationDiagnosisApplicability.EvaluateAsync(dbContext, attempt, cancellationToken)
                        != VerificationDiagnosisApplicability.Verdict.VerificationEvidenceChanged)
                    {
                        return Result.Failure(Error.Conflict(
                            "attempts.not_eligible", "The pinned verification evidence has not changed."));
                    }

                    outcome = AgentOutcome.VerificationEvidenceChanged;
                    break;
                }

            default:
                return Result.Failure(Error.Failure("agent_attempts.invalid_outcome", "The refusal is not a defined dispatch refusal."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        attempt.CompleteAgent(outcome, completionFingerprintSha256: null, nowUtc);
        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(),
            command.RunId,
            command.AttemptId,
            RunEventType.AgentAttemptCompleted,
            ParticipantIdentity.ForOrchestrator(),
            JsonSerializer.Serialize(new { status = attempt.Status.ToString(), outcome = attempt.AgentOutcome.ToString() }),
            nowUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
