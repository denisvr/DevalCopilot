using System.Data.Common;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionAdmissionHistory;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection.LocalExecutionInspectionHarness;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueueInspection.LocalExecutionQueueInspectionHarness;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueueInspection;

/// <summary>
/// The candidate membership, every registered project label, every sequence header and ledger and every historical reference of the
/// overview come from one database statement (ADR-0064), so a competing transition committed through an independent connection is seen
/// entirely or not at all and never as a mixed reading. Each test synchronizes at an exact point of the real read, proves the competing
/// change really happened there and that the read was one statement naming the admissions (so a hook that silently stops firing cannot
/// turn the test vacuous), and then asserts the coherent answer. Separate later requests stay independent observations; nothing here
/// claims an atomic replay of everything.
/// </summary>
public sealed class LocalExecutionQueueInspectionConsistencyTests : IAsyncLifetime
{
    private const string AdmissionTable = "\"local_execution_admissions\"";

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    /// <summary>Runs a competing change once, immediately before the one statement that reads the admissions executes.</summary>
    private sealed class BeforeStatement(Func<Task> compete) : DbCommandInterceptor
    {
        public List<string> Statements { get; } = [];

        public bool Competed { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(AdmissionTable, StringComparison.Ordinal))
            {
                Statements.Add(command.CommandText);
                if (!Competed)
                {
                    Competed = true;
                    await compete();
                }
            }

            return result;
        }
    }

    /// <summary>Once the statement is running, with its reader open, an independent connection tries to commit a write with a short busy
    /// timeout and records whether the database admitted it.</summary>
    private sealed class DuringStatement(string connectionString, string sql) : DbCommandInterceptor
    {
        public List<string> Statements { get; } = [];

        public SqliteException? Refused { get; private set; }

        public bool Attempted { get; private set; }

        public string? JournalMode { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains(AdmissionTable, StringComparison.Ordinal))
            {
                return result;
            }

            Statements.Add(command.CommandText);
            if (Attempted)
            {
                return result;
            }

            Attempted = true;
            await using var connection = new SqliteConnection(connectionString + ";Pooling=False;Default Timeout=1");
            await connection.OpenAsync(cancellationToken);
            await using var mode = connection.CreateCommand();
            mode.CommandText = "PRAGMA journal_mode;";
            JournalMode = (string?)await mode.ExecuteScalarAsync(cancellationToken);
            await using var write = connection.CreateCommand();
#pragma warning disable CA2100 // Test-owned, fixed statement text.
            write.CommandText = sql;
#pragma warning restore CA2100
            try
            {
                await write.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException exception)
            {
                Refused = exception;
            }

            return result;
        }
    }

    // ---- competing production transitions committed just before the statement -----------------------------------------------------------

    [Fact]
    public async Task A_stop_committed_just_before_the_statement_removes_exactly_that_cycle_with_its_admission_and_header_together()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (stopped, stoppedSequence) = await harness.AddCycleAsync();
        var (kept, _) = await harness.AddCycleAsync();
        var hook = new BeforeStatement(async () => Assert.True((await stopped.Scene.StopAsync(stoppedSequence)).IsSuccess));

        var result = await harness.InspectAsync(hook);

        Assert.True(hook.Competed, "The competing stop was never committed at the synchronization point.");
        Assert.Single(hook.Statements);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(kept.Scene.RunId, entry.RunId);
        await AssertMatchesPerRunLedgerAsync(entry, kept);
        Assert.Equal(LocalExecutionAdmissionState.Retired, (await stopped.LastAdmissionAsync()).State);
    }

    [Fact]
    public async Task A_reservation_committed_just_before_the_statement_shows_the_reserved_admission_with_its_new_step_and_header_together()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (cycle, _) = await harness.AddCycleAsync();
        var reserved = Guid.Empty;
        var hook = new BeforeStatement(async () => reserved = (await cycle.Scene.ReserveAsync())!.StepId);

        var result = await harness.InspectAsync(hook);

        Assert.True(hook.Competed);
        Assert.Single(hook.Statements);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(LocalExecutionSequenceState.PreparingStep, entry.Sequence.RecordedState);
        Assert.Equal(LocalExecutionAdmissionState.Reserved, entry.Admission.RecordedState);
        Assert.Equal(reserved, entry.Admission.StepId);
        await AssertMatchesPerRunLedgerAsync(entry, cycle);
    }

    [Fact]
    public async Task A_cycle_queued_just_before_the_statement_is_listed_whole_with_its_project_label_and_header()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (first, _) = await harness.AddCycleAsync(projectName: "First");
        LocalExecutionAdmissionHistoryHarness? late = null;
        var hook = new BeforeStatement(async () => late = (await harness.AddCycleAsync(projectName: "Late")).Cycle);

        var result = await harness.InspectAsync(hook);

        Assert.True(hook.Competed);
        Assert.Single(hook.Statements);
        Assert.Equal(["First", "Late"], result.Entries.Select(entry => entry.ProjectName));
        await AssertMatchesPerRunLedgerAsync(result.Entries[0], first);
        await AssertMatchesPerRunLedgerAsync(result.Entries[1], late!);
    }

    [Fact]
    public async Task A_waiting_reason_change_committed_before_the_statement_updates_the_admission_without_the_sequence_revision()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (cycle, _) = await harness.AddCycleAsync();
        var before = Assert.Single((await harness.InspectAsync()).Entries);
        await cycle.Scene.AddWorkspaceOwnerAsync(OwnerFamily.Agent);
        var hook = new BeforeStatement(() => cycle.Scene.ObserveWholeQueueAsync());

        var result = await harness.InspectAsync(hook);

        Assert.True(hook.Competed);
        Assert.Single(hook.Statements);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(before.Sequence.SequenceRevision, entry.Sequence.SequenceRevision);
        Assert.Equal(LocalExecutionWaitingReason.WaitingForWorkspace, entry.Admission.WaitingReason);
        Assert.Equal(before.Admission.Revision + 1, entry.Admission.Revision);
        await AssertMatchesPerRunLedgerAsync(entry, cycle);
    }

    [Fact]
    public async Task A_successor_recorded_just_before_the_statement_replaces_the_consumed_tail_with_its_new_waiting_admission()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (cycle, sequenceId) = await harness.AddCycleAsync();
        var ticket = (await cycle.Scene.ReserveAsync())!;
        await cycle.Scene.DriveTicketAsync(ticket, new LocalExecutionStepScript());
        AssertShowsNothing(await harness.InspectAsync(), LocalExecutionQueueInspectionStatus.Empty);
        var hook = new BeforeStatement(async () => Assert.True((await cycle.Scene.ObserveAsync(sequenceId)).IsSuccess));

        var result = await harness.InspectAsync(hook);

        Assert.True(hook.Competed);
        Assert.Single(hook.Statements);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(1, entry.Admission.Ordinal);
        Assert.Equal(LocalExecutionAdmissionState.Waiting, entry.Admission.RecordedState);
        await AssertMatchesPerRunLedgerAsync(entry, cycle);
    }

    // ---- one statement reads every joined fact ----------------------------------------------------------------------------------------

    public static TheoryData<string> JoinedFacts =>
    [
        "candidate",
        "sequence",
        "run",
        "project",
        "step",
        "initial-checkpoint",
        "source-checkpoint",
        "workspace",
        "report-message",
        "recipe",
        "admission",
    ];

    private static (string Sql, string[] Triggers) Competing(string fact, Guid sequenceId, Guid runId, Guid projectId)
    {
        var seq = $"'{sequenceId.ToString("D").ToUpperInvariant()}'";
        var run = $"'{runId.ToString("D").ToUpperInvariant()}'";
        var project = $"'{projectId.ToString("D").ToUpperInvariant()}'";
        return fact switch
        {
            "candidate" => (
                $"UPDATE local_execution_admissions SET State = 'Bogus' WHERE SequenceId = {seq} AND Ordinal = 9",
                [LocalExecutionAdmissionHistoryHarness.AdmissionImmutable]),
            "sequence" => ($"UPDATE local_execution_sequences SET AdmissionMode = 'Bogus' WHERE Id = {seq}", [SequenceImmutable]),
            "run" => ($"UPDATE runs SET ExecutionMode = '1' WHERE Id = {run}", []),
            "project" => ($"UPDATE projects SET Name = '   ' WHERE Id = {project}", []),
            "step" => (
                $"UPDATE local_execution_sequence_steps SET Kind = 'CodeReview' WHERE SequenceId = {seq} AND Ordinal = 2", [StepImmutable]),
            "initial-checkpoint" => (
                $"UPDATE git_checkpoints SET FingerprintSha256 = '{new string('d', 64)}' WHERE Id = (SELECT CheckpointId FROM local_execution_sequences WHERE Id = {seq})",
                []),
            "source-checkpoint" => (
                $"UPDATE git_checkpoints SET FingerprintSha256 = '{new string('d', 64)}' WHERE Id = (SELECT SourceCheckpointId FROM local_execution_admissions WHERE SequenceId = {seq} AND Ordinal = 5)",
                []),
            "workspace" => (
                "UPDATE git_workspaces SET ProjectId = (SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4000-8000-' || lower(hex(randomblob(6)))) "
                + $"WHERE Id = (SELECT WorkspaceId FROM local_execution_sequences WHERE Id = {seq})",
                []),
            "report-message" => (
                $"UPDATE collaboration_messages SET Type = 'Acceptance' WHERE Id = (SELECT ReportMessageId FROM local_execution_admissions WHERE SequenceId = {seq} AND Ordinal = 7)",
                []),
            "recipe" => (
                $"UPDATE local_execution_sequence_recipes SET CommandName = '   ' WHERE SequenceId = {seq} AND Ordinal = 0", [RecipeImmutable]),
            "admission" => (
                $"UPDATE local_execution_admissions SET Kind = 'CodeReview' WHERE SequenceId = {seq} AND Ordinal = 2",
                [LocalExecutionAdmissionHistoryHarness.AdmissionImmutable]),
            _ => throw new ArgumentOutOfRangeException(nameof(fact)),
        };
    }

    private async Task<(LocalExecutionQueueInspectionHarness Harness, LocalExecutionAdmissionHistoryHarness Cycle, Guid SequenceId)> LongCycleAsync()
    {
        var harness = new LocalExecutionQueueInspectionHarness(fixture);
        var (cycle, sequenceId) = await harness.AddCycleAsync(recipeCount: 2);
        await cycle.DriveStepsThenWaitAsync(sequenceId, 9, LocalExecutionAdmissionHistoryHarness.MixedOriginsScript());
        await harness.AddCycleAsync();
        Assert.Equal(LocalExecutionQueueInspectionStatus.Recorded, (await harness.InspectAsync()).Status);
        return (harness, cycle, sequenceId);
    }

    [Theory]
    [MemberData(nameof(JoinedFacts))]
    public async Task A_competing_change_to_each_joined_fact_committed_before_the_statement_is_read_by_that_same_statement(string fact)
    {
        var (harness, cycle, sequenceId) = await LongCycleAsync();
        var (sql, triggers) = Competing(fact, sequenceId, cycle.Scene.RunId, cycle.Scene.ProjectId);
        var hook = new BeforeStatement(() => harness.CorruptAsync(sql, null, triggers));

        var result = await harness.InspectAsync(hook);

        Assert.True(hook.Competed, "The competing change was never committed at the synchronization point.");
        var statement = Assert.Single(hook.Statements);
        Assert.Contains("LIMIT", statement, StringComparison.Ordinal);
        AssertShowsNothing(result, LocalExecutionQueueInspectionStatus.Unavailable);
    }

    // ---- a write during the statement never reaches its answer ---------------------------------------------------------------------------

    [Fact]
    public async Task A_write_committed_while_the_statement_is_running_never_reaches_the_answer_which_stays_the_coherent_prior_snapshot()
    {
        var (harness, cycle, _) = await LongCycleAsync();
        var before = await harness.InspectAsync();
        var hook = new DuringStatement(
            fixture.ConnectionString,
            $"UPDATE projects SET Name = '   ' WHERE Id = '{cycle.Scene.ProjectId.ToString("D").ToUpperInvariant()}'");

        var during = await harness.InspectAsync(hook);

        // The production database runs in write-ahead logging, where a writer is never blocked by a reader and a reader never sees a
        // commit that happened after its statement started: the write below really commits while the overview is still being read, and
        // it would make the answer unprovable if any later part of it read the project.
        Assert.True(hook.Attempted, "The competing write was never attempted while the statement was running.");
        Assert.Single(hook.Statements);
        Assert.Equal("wal", hook.JournalMode);
        Assert.Null(hook.Refused);
        Assert.Equal(LocalExecutionQueueInspectionStatus.Recorded, during.Status);
        Assert.Equal(before.Entries, during.Entries);

        // The next, separate request is a new observation and sees the committed change.
        AssertShowsNothing(await harness.InspectAsync(), LocalExecutionQueueInspectionStatus.Unavailable);
    }
}
