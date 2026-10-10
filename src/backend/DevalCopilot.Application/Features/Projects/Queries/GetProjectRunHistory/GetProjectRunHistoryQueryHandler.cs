using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectRunHistory;

public sealed class GetProjectRunHistoryQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetProjectRunHistoryQuery, Result<GetProjectRunHistoryQueryResult>>
{
    // The database classifies each enum column into one of these codes (0: not a value this version knows). The stored text is never
    // parsed into an enum, so an unrecognized lifecycle or stage keeps its row instead of failing the page.
    private sealed record RunRow(
        Guid Id,
        int ExecutionNumber,
        string Objective,
        int LifecycleCode,
        int StageCode,
        string? StoredExecutionMode,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset LastAdvancedAtUtc);

    private sealed record OperationRow(
        Guid Id,
        Guid RunId,
        Guid ProjectId,
        string CommitSha,
        Guid CheckpointId,
        int CheckpointNumber,
        bool IsCompleted,
        bool HasCompletionTime);

    public async Task<Result<GetProjectRunHistoryQueryResult>> HandleAsync(
        GetProjectRunHistoryQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Projects.AsNoTracking().AnyAsync(project => project.Id == query.ProjectId, cancellationToken))
        {
            return Result<GetProjectRunHistoryQueryResult>.Failure(
                Error.NotFound("projects.not_found", "This project does not exist."));
        }

        var limit = query.Limit ?? GetProjectRunHistoryQuery.DefaultLimit;
        var candidates = dbContext.Runs.AsNoTracking().Where(run => run.ProjectId == query.ProjectId);
        if (query.BeforeExecutionNumber is { } before)
        {
            candidates = candidates.Where(run => run.ExecutionNumber < before);
        }

        // One extra row proves whether older runs remain without a count. The unique (ProjectId, ExecutionNumber) index makes the
        // descending order total and the exclusive cursor stable while newer runs are created. Only the columns an entry shows are
        // read, never the Run aggregate.
        var rows = await candidates
            .OrderByDescending(run => run.ExecutionNumber)
            .Take(limit + 1)
            .Select(run => new RunRow(
                run.Id,
                run.ExecutionNumber,
                run.Objective,
                run.Lifecycle == RunLifecycle.Created ? 1
                    : run.Lifecycle == RunLifecycle.Running ? 2
                    : run.Lifecycle == RunLifecycle.Completed ? 3
                    : run.Lifecycle == RunLifecycle.Failed ? 4
                    : run.Lifecycle == RunLifecycle.Interrupted ? 5
                    : run.Lifecycle == RunLifecycle.Abandoned ? 6
                    : 0,
                run.Stage == RunStage.Intake ? 1
                    : run.Stage == RunStage.Plan ? 2
                    : run.Stage == RunStage.Critique ? 3
                    : run.Stage == RunStage.Resolution ? 4
                    : run.Stage == RunStage.Execute ? 5
                    : run.Stage == RunStage.Completed ? 6
                    : 0,
                EF.Property<string>(run, Run.ExecutionModeStorageProperty),
                run.CreatedAtUtc,
                run.LastAdvancedAtUtc))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > limit;
        var page = hasMore ? rows.GetRange(0, limit) : rows;

        // Each run is matched to its OWN recorded operation by its identity, never to the latest operation, the current checkpoint or
        // another run's delivery. The one operation per run is guaranteed by a unique index; the status is classified by the database.
        var runIds = page.Select(row => row.Id).ToList();
        var operations = runIds.Count == 0
            ? []
            : await dbContext.LocalCommitOperations.AsNoTracking()
                .Where(operation => runIds.Contains(operation.RunId))
                .Select(operation => new OperationRow(
                    operation.Id,
                    operation.RunId,
                    operation.ProjectId,
                    operation.CommitSha,
                    operation.GitCheckpointId,
                    operation.CheckpointNumber,
                    operation.Status == LocalCommitStatus.Completed,
                    operation.CompletedAtUtc != null))
                .ToListAsync(cancellationToken);

        var entries = page
            .Select(row => new ProjectRunHistoryEntry(
                row.Id,
                row.ExecutionNumber,
                row.Objective,
                ToLifecycle(row.LifecycleCode),
                ToStage(row.StageCode),
                RunExecutionModeStorage.Read(row.StoredExecutionMode),
                row.CreatedAtUtc,
                row.LastAdvancedAtUtc,
                ToSource(row, query.ProjectId, operations.Where(operation => operation.RunId == row.Id).ToList())))
            .ToList();

        return Result<GetProjectRunHistoryQueryResult>.Success(new GetProjectRunHistoryQueryResult(
            query.ProjectId, entries, hasMore, hasMore ? entries[^1].ExecutionNumber : null));
    }

    private static RunLifecycle? ToLifecycle(int code) => code switch
    {
        1 => RunLifecycle.Created,
        2 => RunLifecycle.Running,
        3 => RunLifecycle.Completed,
        4 => RunLifecycle.Failed,
        5 => RunLifecycle.Interrupted,
        6 => RunLifecycle.Abandoned,
        _ => null,
    };

    private static RunStage? ToStage(int code) => code switch
    {
        1 => RunStage.Intake,
        2 => RunStage.Plan,
        3 => RunStage.Critique,
        4 => RunStage.Resolution,
        5 => RunStage.Execute,
        6 => RunStage.Completed,
        _ => null,
    };

    /// <summary>The source exists only for a run recorded as Completed (lifecycle and stage) that owns exactly one operation, which is
    /// recorded as Completed with a completion time, belongs to this same project and run, and carries structurally valid identities.
    /// Anything else is no source: that says nothing about whether an operation exists, failed or succeeded.</summary>
    private static ProjectRunHistoryReceiptSource? ToSource(RunRow run, Guid projectId, List<OperationRow> operations)
    {
        if (run.LifecycleCode != 3 || run.StageCode != 6 || operations.Count != 1)
        {
            return null;
        }

        var operation = operations[0];
        return operation.IsCompleted && operation.HasCompletionTime && operation.RunId == run.Id && operation.ProjectId == projectId
            && operation.Id != Guid.Empty && operation.CheckpointId != Guid.Empty && operation.CheckpointNumber >= 1
            && IsObjectId(operation.CommitSha)
                ? new ProjectRunHistoryReceiptSource(
                    operation.RunId, operation.Id, operation.CommitSha, operation.CheckpointId, operation.CheckpointNumber)
                : null;
    }

    private static bool IsObjectId(string? value) =>
        value is { Length: 40 } && value.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
