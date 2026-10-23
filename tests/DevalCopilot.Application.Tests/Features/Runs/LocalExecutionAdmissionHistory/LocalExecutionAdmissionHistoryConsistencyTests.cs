using System.Data.Common;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionAdmissionHistory;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionAdmissionHistory.LocalExecutionAdmissionHistoryHarness;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection.LocalExecutionInspectionHarness;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionAdmissionHistory;

/// <summary>
/// The ledger, its root and every historical fact it is proved against come from one database statement (ADR-0063), so a competing
/// transition through an independent connection is seen entirely or not at all and never as a mixed reading. Each test synchronizes at
/// an exact point of the real reader, proves the competing change really happened there and that the read was one statement naming the
/// joined tables (so a hook that silently stops firing cannot turn the test vacuous), and then asserts the coherent answer. Separate
/// later requests stay independent observations; nothing here claims an atomic replay of everything.
/// </summary>
public sealed class LocalExecutionAdmissionHistoryConsistencyTests : IAsyncLifetime
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

    private async Task<(LocalExecutionAdmissionHistoryHarness Harness, Guid SequenceId)> EndedCycleAsync()
    {
        var (harness, sequenceId) = await EndedAsync(fixture, 2, MixedOriginsScript());
        return (harness, sequenceId);
    }

    [Fact]
    public async Task A_stop_committed_just_before_the_statement_is_read_whole_with_its_admission_and_header_together()
    {
        var (harness, sequenceId) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);
        var hook = new BeforeStatement(async () => Assert.True((await harness.Scene.StopAsync(sequenceId)).IsSuccess));

        var history = await harness.HistoryAsync(null, hook);

        Assert.True(hook.Competed, "The competing stop was never committed at the synchronization point.");
        Assert.Single(hook.Statements);
        await harness.AssertMirrorsStorageAsync(history);
        Assert.Equal(LocalExecutionSequenceState.Stopped, history.Sequence!.RecordedState);
        Assert.Equal(LocalExecutionAdmissionState.Retired, Assert.Single(history.Admissions).RecordedState);
        Assert.Null(Assert.Single(history.Admissions).StepId);
    }

    [Fact]
    public async Task A_reservation_committed_just_before_the_statement_shows_the_reserved_admission_with_its_new_step_together()
    {
        var (harness, _) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);
        var reserved = Guid.Empty;
        var hook = new BeforeStatement(async () => reserved = (await harness.Scene.ReserveAsync())!.StepId);

        var history = await harness.HistoryAsync(null, hook);

        Assert.True(hook.Competed);
        Assert.Single(hook.Statements);
        await harness.AssertMirrorsStorageAsync(history);
        Assert.Equal(LocalExecutionSequenceState.PreparingStep, history.Sequence!.RecordedState);
        var member = Assert.Single(history.Admissions);
        Assert.Equal(LocalExecutionAdmissionState.Reserved, member.RecordedState);
        Assert.Equal(reserved, member.StepId);
    }

    [Fact]
    public async Task A_waiting_reason_change_committed_before_the_statement_updates_the_admission_without_the_sequence_revision()
    {
        var (harness, _) = await LocalExecutionAdmissionHistoryHarness.QueuedAsync(fixture);
        var before = await harness.HistoryAsync();
        await harness.Scene.AddWorkspaceOwnerAsync(OwnerFamily.Agent);
        var hook = new BeforeStatement(() => harness.Scene.ObserveWholeQueueAsync());

        var history = await harness.HistoryAsync(null, hook);

        Assert.True(hook.Competed);
        Assert.Single(hook.Statements);
        await harness.AssertMirrorsStorageAsync(history);
        Assert.Equal(before.Sequence!.SequenceRevision, history.Sequence!.SequenceRevision);
        Assert.Equal(LocalExecutionWaitingReason.WaitingForWorkspace, Assert.Single(history.Admissions).WaitingReason);
        Assert.Equal(Assert.Single(before.Admissions).Revision + 1, Assert.Single(history.Admissions).Revision);
    }

    [Fact]
    public async Task A_write_committed_while_the_statement_is_running_never_reaches_the_answer_which_stays_the_coherent_prior_snapshot()
    {
        var (harness, _) = await EndedCycleAsync();
        var before = await harness.HistoryAsync();
        var hook = new DuringStatement(
            fixture.ConnectionString,
            "UPDATE collaboration_messages SET Type = 'Acceptance' WHERE Id = (SELECT ReportMessageId FROM local_execution_admissions WHERE \"Ordinal\" = 7)");

        var during = await harness.HistoryAsync(null, hook);

        // The production database runs in write-ahead logging, where a writer is never blocked by a reader and a reader never sees a
        // commit that happened after its statement started: the write below really commits while the ledger is still being read, and
        // it would make the ledger unprovable if any later part of this answer read it.
        Assert.True(hook.Attempted, "The competing write was never attempted while the statement was running.");
        Assert.Single(hook.Statements);
        Assert.Equal("wal", hook.JournalMode);
        Assert.Null(hook.Refused);
        Assert.Equal(LocalExecutionAdmissionHistoryStatus.Recorded, during.Status);
        Assert.Equal(before.Sequence, during.Sequence);
        Assert.Equal(before.Admissions, during.Admissions);

        // The next, separate request is a new observation and sees the committed change.
        AssertShowsNothing(await harness.HistoryAsync(), LocalExecutionAdmissionHistoryStatus.Unavailable);
    }

    public static TheoryData<string> JoinedFacts =>
    [
        "sequence",
        "run",
        "step",
        "initial-checkpoint",
        "source-checkpoint",
        "workspace",
        "report-message",
        "recipe",
        "admission",
    ];

    private static (string Sql, string[] Triggers) Competing(string fact) => fact switch
    {
        "sequence" => ("UPDATE local_execution_sequences SET AdmissionMode = 'Bogus'", [SequenceImmutable]),
        "run" => ("UPDATE runs SET ExecutionMode = '1'", []),
        "step" => ("UPDATE local_execution_sequence_steps SET Kind = 'CodeReview' WHERE Ordinal = 2", [StepImmutable]),
        "initial-checkpoint" => (
            "UPDATE git_checkpoints SET FingerprintSha256 = '" + new string('d', 64)
            + "' WHERE Id = (SELECT CheckpointId FROM local_execution_sequences)", []),
        "source-checkpoint" => (
            "UPDATE git_checkpoints SET FingerprintSha256 = '" + new string('d', 64)
            + "' WHERE Id = (SELECT SourceCheckpointId FROM local_execution_admissions WHERE Ordinal = 5)", []),
        "workspace" => (
            "UPDATE git_workspaces SET ProjectId = (SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4000-8000-' || lower(hex(randomblob(6)))) "
            + "WHERE Id = (SELECT WorkspaceId FROM local_execution_sequences)", []),
        "report-message" => (
            "UPDATE collaboration_messages SET Type = 'Acceptance' WHERE Id = (SELECT ReportMessageId FROM local_execution_admissions WHERE Ordinal = 7)", []),
        "recipe" => ("UPDATE local_execution_sequence_recipes SET CommandName = '   ' WHERE Ordinal = 1", [RecipeImmutable]),
        "admission" => ("UPDATE local_execution_admissions SET Kind = 'CodeReview' WHERE Ordinal = 2", [AdmissionImmutable]),
        _ => throw new ArgumentOutOfRangeException(nameof(fact)),
    };

    [Theory]
    [MemberData(nameof(JoinedFacts))]
    public async Task A_competing_change_to_each_joined_fact_committed_before_the_statement_is_read_by_that_same_statement(string fact)
    {
        var (harness, _) = await EndedCycleAsync();
        Assert.Equal(LocalExecutionAdmissionHistoryStatus.Recorded, (await harness.HistoryAsync()).Status);
        var (sql, triggers) = Competing(fact);
        var hook = new BeforeStatement(() => harness.CorruptAsync(sql, null, triggers));

        var history = await harness.HistoryAsync(null, hook);

        Assert.True(hook.Competed, "The competing change was never committed at the synchronization point.");
        var statement = Assert.Single(hook.Statements);
        Assert.Contains("LIMIT", statement, StringComparison.Ordinal);
        AssertShowsNothing(history, LocalExecutionAdmissionHistoryStatus.Unavailable);
    }
}
