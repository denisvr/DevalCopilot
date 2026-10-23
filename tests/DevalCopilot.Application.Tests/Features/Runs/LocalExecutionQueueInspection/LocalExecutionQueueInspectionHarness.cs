using System.Data.Common;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionAdmissionHistory;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueueInspection;

/// <summary>
/// The reusable harness of the ADR-0064 outstanding-admission tests: one real-file-backed-SQLite database that holds several queued
/// cycles in distinct projects, every one created and moved by the production commands (the ADR-0062 scene), the real handler over a
/// context of its own and a corruption helper that writes damaged storage through a separate connection. Nothing here writes through the
/// overview handler; the scenes alone change the world, exactly as another request would.
/// </summary>
internal sealed class LocalExecutionQueueInspectionHarness(SqliteDatabaseFixture fixture)
{
    public SqliteDatabaseFixture Fixture { get; } = fixture;

    public List<(LocalExecutionAdmissionHistoryHarness Cycle, Guid SequenceId)> Cycles { get; } = [];

    /// <summary>One more queued cycle in a project of its own, created by the production arming command in WaitForAdmission mode. A
    /// project name is renamed through a test-only storage write when a distinct label is wanted.</summary>
    public async Task<(LocalExecutionAdmissionHistoryHarness Cycle, Guid SequenceId)> AddCycleAsync(
        int recipeCount = 2, string? projectName = null)
    {
        var cycle = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(Fixture, recipeCount);
        Cycles.Add(cycle);
        if (projectName is not null)
        {
            await SetProjectNameAsync(cycle.Harness.Scene.ProjectId, projectName);
        }

        return cycle;
    }

    public async Task SetProjectNameAsync(Guid projectId, string name)
    {
        await using var context = Fixture.CreateContext();
        var affected = await context.Database.ExecuteSqlRawAsync(
            "UPDATE projects SET Name = {0} WHERE Id = {1}", name, projectId.ToString("D").ToUpperInvariant());
        Assert.Equal(1, affected);
    }

    public async Task<GetLocalExecutionQueueInspectionQueryResult> InspectAsync(params IInterceptor[] interceptors)
    {
        await using var context = Fixture.CreateContext(interceptors);
        var result = await new GetLocalExecutionQueueInspectionQueryHandler(context).HandleAsync(
            new GetLocalExecutionQueueInspectionQuery(), CancellationToken.None);
        Assert.Empty(context.ChangeTracker.Entries());
        return result;
    }

    /// <summary>Writes damaged storage through a connection of its own, exactly as the per-run harness does.</summary>
    public Task CorruptAsync(string sql, object?[]? values = null, params string[] triggersToDrop) =>
        Cycles[0].Cycle.CorruptAsync(sql, values, triggersToDrop);

    /// <summary>Every durable fact an overview read could disturb, as one comparable value: row counts of the events, messages,
    /// attempts, executions and outputs, and the state, revision and update time of every sequence, step and admission.</summary>
    public async Task<string> ChangeFingerprintAsync()
    {
        await using var context = Fixture.CreateContext();
        var events = await context.Events.AsNoTracking().CountAsync();
        var messages = await context.CollaborationMessages.AsNoTracking().CountAsync();
        var attempts = await context.Attempts.AsNoTracking().CountAsync();
        var executions = await context.VerificationExecutions.AsNoTracking().CountAsync();
        var outputs = await context.VerificationOutputArtifacts.AsNoTracking().CountAsync();
        var sequences = (await context.LocalExecutionSequences.AsNoTracking().ToListAsync())
            .OrderBy(sequence => sequence.CoordinationNumber).Select(sequence => $"{sequence.Revision}:{sequence.State}:{sequence.UpdatedAtUtc:O}");
        var steps = (await context.LocalExecutionSequenceSteps.AsNoTracking().ToListAsync())
            .OrderBy(step => step.ReservedOperationId).Select(step => $"{step.Revision}:{step.State}:{step.UpdatedAtUtc:O}");
        var admissions = (await context.LocalExecutionAdmissions.AsNoTracking().ToListAsync())
            .OrderBy(admission => admission.AdmissionNumber)
            .Select(admission => $"{admission.AdmissionNumber}:{admission.Revision}:{admission.State}:{admission.WaitingReason}");
        var counter = await context.LocalExecutionOrderCounters.AsNoTracking().Select(row => row.LastNumber + ":" + row.Revision).ToListAsync();
        return string.Join(
            '|', events, messages, attempts, executions, outputs, string.Join(',', sequences), string.Join(',', steps),
            string.Join(',', admissions), string.Join(',', counter));
    }

    /// <summary>Every answer that is not Recorded shows no entry: never a partial list or count.</summary>
    public static void AssertShowsNothing(GetLocalExecutionQueueInspectionQueryResult result, LocalExecutionQueueInspectionStatus status)
    {
        Assert.Equal(status, result.Status);
        Assert.Empty(result.Entries);
    }

    /// <summary>The entry equals the proved tail of the per-run ADR-0063 ledger of the same run, field by field.</summary>
    public static async Task AssertMatchesPerRunLedgerAsync(
        LocalExecutionQueueInspectionEntry entry, LocalExecutionAdmissionHistoryHarness cycle)
    {
        var history = await cycle.HistoryAsync();
        Assert.Equal(DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionAdmissionHistory.LocalExecutionAdmissionHistoryStatus.Recorded, history.Status);
        Assert.Equal(cycle.Scene.RunId, entry.RunId);
        Assert.Equal(cycle.Scene.ProjectId, entry.ProjectId);
        Assert.Equal(history.Sequence, entry.Sequence);
        Assert.Equal(history.Admissions[^1], entry.Admission);
    }

    /// <summary>Records every command the context sends, so a test proves one statement and what it named.</summary>
    public sealed class CommandRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }
    }

}
