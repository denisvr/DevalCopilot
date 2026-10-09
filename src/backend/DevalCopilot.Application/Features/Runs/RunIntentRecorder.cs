using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.Abandonment;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The single creation path for a new Run, shared by the manual and simulated creation operations so
/// both apply the same admission rule. A project admits a new intent only when it has no run, or only
/// runs whose lifecycle is a recognized terminal one; any Created, Running, or unrecognized lifecycle
/// blocks it. This is conservative intake admission, never a scheduler or a replacement of an active run.
///
/// <para>
/// The check and the number reservation are one serialized unit: <c>Project.NextExecutionNumber</c> is an
/// EF concurrency token, so two requests that both passed the check and read the same counter cannot both
/// commit. The loser's UPDATE matches no row, the whole save rolls back (run, event, and counter together),
/// and it is reported as a retryable conflict. A refusal writes nothing. The caller supplies the effective, already validated
/// run-wide Agent claim and reserved-time ceilings (ADR-0028); the simulated operation passes the fixed defaults.
/// </para>
/// </summary>
public static class RunIntentRecorder
{
    public const string BlockedCode = "runs.intent_blocked";
    public const string ConcurrentIntentCode = "runs.intent_conflict";

    public static async Task<Result<RecordedRunIntent>> RecordAsync(
        IDevalCopilotDbContext dbContext,
        TimeProvider timeProvider,
        Guid projectId,
        string objective,
        RunExecutionMode executionMode,
        int maximumAgentAttempts,
        TimeSpan maximumAgentInvocationTime,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .SingleOrDefaultAsync(candidate => candidate.Id == projectId, cancellationToken);
        if (project is null)
        {
            return Result<RecordedRunIntent>.Failure(
                Error.NotFound("projects.not_found", "The requested project was not found."));
        }

        // Filtered in SQL against the terminal set so a stored lifecycle this build does not
        // recognize is never materialized and still blocks. Abandoned is terminal only through its
        // coherent facts (ADR-0031): the lifecycle alone never admits another intent.
        var blocked = await dbContext.Runs.AsNoTracking().AnyAsync(
            candidate => candidate.ProjectId == project.Id
                && candidate.Lifecycle != RunLifecycle.Completed
                && candidate.Lifecycle != RunLifecycle.Failed
                && candidate.Lifecycle != RunLifecycle.Interrupted
                && candidate.Lifecycle != RunLifecycle.Abandoned,
            cancellationToken);
        if (!blocked)
        {
            blocked = (await RunAbandonmentReader.ReadForProjectAsync(dbContext, project.Id, cancellationToken))
                .Any(reading => !reading.Coherent);
        }

        if (blocked)
        {
            return Result<RecordedRunIntent>.Failure(Error.Conflict(
                BlockedCode,
                "This project already has a run that is not finished, so a new objective cannot be recorded."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        var executionNumber = project.ReserveExecutionNumber();
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(),
            project.Id,
            executionNumber,
            executionMode,
            objective,
            nowUtc,
            maximumAgentAttempts: maximumAgentAttempts,
            maximumAgentInvocationTime: maximumAgentInvocationTime);
        var runEvent = RunEvent.Record(
            Guid.NewGuid(),
            run.Id,
            attemptId: null,
            RunEventType.RunStarted,
            ParticipantIdentity.ForOrchestrator(),
            JsonSerializer.Serialize(new { objective }),
            nowUtc);
        dbContext.Runs.Add(run);
        dbContext.Events.Add(runEvent);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.Events.Remove(runEvent);
            dbContext.Runs.Remove(run);
            return Result<RecordedRunIntent>.Failure(Error.Conflict(
                ConcurrentIntentCode,
                "Another objective was being recorded for this project at the same time; retry the request."));
        }

        return Result<RecordedRunIntent>.Success(new RecordedRunIntent(run.Id, run.ExecutionNumber));
    }
}
