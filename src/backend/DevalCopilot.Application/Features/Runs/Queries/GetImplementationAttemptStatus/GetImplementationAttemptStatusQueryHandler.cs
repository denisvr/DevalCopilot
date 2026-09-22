using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetImplementationAttemptStatus;

public sealed class GetImplementationAttemptStatusQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetImplementationAttemptStatusQuery, Result<ImplementationAttemptStatusQueryResult>>
{
    public async Task<Result<ImplementationAttemptStatusQueryResult>> HandleAsync(
        GetImplementationAttemptStatusQuery query, CancellationToken cancellationToken)
    {
        var runExists = await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken);
        if (!runExists)
        {
            return Result<ImplementationAttemptStatusQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        Attempt? attempt;
        try
        {
            attempt = await dbContext.Attempts
                .AsNoTracking()
                .Where(candidate =>
                    candidate.RunId == query.RunId && candidate.Kind == AttemptKind.Agent && candidate.AgentRole == AgentRole.Implementer)
                .OrderByDescending(candidate => candidate.AttemptNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            return InvalidAssignment();
        }
        catch (FormatException)
        {
            return InvalidAssignment();
        }
        catch (InvalidOperationException)
        {
            return InvalidAssignment();
        }

        if (attempt is null)
        {
            return Result<ImplementationAttemptStatusQueryResult>.Success(ImplementationAttemptStatusQueryResult.NoAttempt);
        }

        var assignment = attempt.GetAssignmentSnapshot();
        if (assignment is null)
        {
            return InvalidAssignment();
        }

        var planProposalMessageId = await ImplementationInputIdentity.GetPlanProposalMessageIdAsync(dbContext, attempt.Id, cancellationToken);

        var artifacts = await dbContext.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.AttemptId == attempt.Id)
            .Select(artifact => new AgentAttemptArtifactMetadata(artifact.Purpose, artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome))
            .ToArrayAsync(cancellationToken);

        string? resultCheckpointFingerprint = null;
        IReadOnlyList<string> changedRelativePaths = [];
        if (attempt.AgentResultGitCheckpointId is { } resultCheckpointId)
        {
            resultCheckpointFingerprint = await dbContext.GitCheckpoints
                .AsNoTracking()
                .Where(checkpoint => checkpoint.Id == resultCheckpointId)
                .Select(checkpoint => checkpoint.FingerprintSha256)
                .SingleOrDefaultAsync(cancellationToken);

            changedRelativePaths = await dbContext.GitChangedFiles
                .AsNoTracking()
                .Where(changedFile => changedFile.CheckpointId == resultCheckpointId)
                .Select(changedFile => changedFile.Path)
                .ToArrayAsync(cancellationToken);
        }

        string? executionReportSummary = null;
        if (attempt.AgentOutcome == AgentOutcome.Implemented)
        {
            executionReportSummary = await dbContext.CollaborationMessages
                .AsNoTracking()
                .Where(message => message.AttemptId == attempt.Id && message.Type == CollaborationMessageType.ExecutionReport)
                .Select(message => message.Summary)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return Result<ImplementationAttemptStatusQueryResult>.Success(new ImplementationAttemptStatusQueryResult(
            true,
            attempt.Id,
            attempt.AttemptNumber,
            planProposalMessageId,
            attempt.Status,
            attempt.AgentOutcome,
            attempt.AgentGitCheckpointId,
            attempt.AgentCheckpointFingerprintSha256,
            attempt.AgentResultGitCheckpointId,
            resultCheckpointFingerprint,
            executionReportSummary,
            changedRelativePaths,
            attempt.ClaimedAtUtc,
            attempt.AgentDispatchedAtUtc,
            attempt.CompletedAtUtc,
            artifacts,
            assignment,
            attempt.AgentRole));
    }

    private static Result<ImplementationAttemptStatusQueryResult> InvalidAssignment() =>
        Result<ImplementationAttemptStatusQueryResult>.Failure(
            Error.Failure("agent_attempts.invalid_assignment", "Implementation assignment metadata is unavailable."));
}
