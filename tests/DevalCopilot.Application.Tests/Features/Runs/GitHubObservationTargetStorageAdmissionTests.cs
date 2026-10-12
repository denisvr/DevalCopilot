using System.Data.Common;
using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0040 storage admission on a real file-backed SQLite database. A stored actor disclosure this version does not know must make the
/// whole reading incoherent instead of throwing an EF conversion fault, and every stored text the reader materializes is bounded by its
/// stored bytes (<c>length(CAST(x AS BLOB))</c>) first, because SQLite's text <c>length()</c> stops at an embedded NUL and
/// <c>HasMaxLength</c> does not bound SQLite storage. Nothing is defaulted, repaired or partially shown, and nothing is written.
/// </summary>
public sealed class GitHubObservationTargetStorageAdmissionTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class Commands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private async Task<(LocalCommitRows Rows, GitHubObservationTarget Target, GitHubObservationSource Source)> SeedAsync()
    {
        await using var db = _fixture.CreateContext();
        var rows = await LocalCommitRowsSeed.SeedAsync(db, LocalCommitStatus.Completed, WorkspaceStatus.Ready);
        var target = GitHubObservationTarget.Record(
            Guid.NewGuid(), rows.Run.Id, rows.Project.Id, rows.Operation.Id, rows.Workspace.Id, rows.Operation.CommitSha,
            rows.Operation.BranchName, "octo-cat", "hello.world", LocalCommitRowsSeed.Now, Guid.NewGuid());
        db.GitHubObservationTargets.Add(target);
        db.Events.Add(RunEvent.Record(
            target.EventId, rows.Run.Id, null, RunEventType.GitHubObservationTargetRecorded, ParticipantIdentity.ForHuman(),
            GitHubObservationTargetEventPayload.Build(target), LocalCommitRowsSeed.Now));
        await db.SaveChangesAsync();
        var source = new GitHubObservationSource(
            rows.Run.Id, rows.Project.Id, rows.Operation.Id, rows.Workspace.Id, rows.Operation.CommitSha, rows.Operation.BranchName);
        return (rows, target, source);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants and identifiers the test itself generated.
        command.CommandText = sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> FingerprintAsync()
    {
        await using var connection = new SqliteConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT (SELECT count(*) FROM github_observation_targets) || '|' || (SELECT count(*) FROM events) || '|' || (SELECT count(*) FROM runs)";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<(GitHubObservationTargetReading Reading, List<string> Sql)> ReadAsync(GitHubObservationSource source)
    {
        var capture = new Commands();
        await using var db = _fixture.CreateContext(capture);
        var reading = await GitHubObservationTargetReader.ReadAsync(db, source.RunId, source, CancellationToken.None);
        return (reading, capture.Sql);
    }

    [Theory]
    [InlineData("ActorKind")]
    [InlineData("ActorAgentRole")]
    [InlineData("ActorAgentProvider")]
    public async Task An_unknown_stored_actor_disclosure_is_an_incoherent_reading_and_not_a_converter_fault(string column)
    {
        var (_, target, source) = await SeedAsync();
        await ExecuteAsync($"UPDATE events SET \"{column}\" = 'UnrecognizedFutureValue' WHERE \"Id\" = '{target.EventId.ToString().ToUpperInvariant()}'");
        var before = await FingerprintAsync();

        var (reading, _) = await ReadAsync(source);

        Assert.Equal(GitHubObservationTargetReadingKind.Incoherent, reading.Kind);
        Assert.Null(reading.Target);
        Assert.Equal(before, await FingerprintAsync());
    }

    [Theory]
    [InlineData("Human", null, null, true)]
    [InlineData("Agent", null, null, false)]
    [InlineData("Human", "Planner", null, false)]
    [InlineData("Human", null, "Codex", false)]
    [InlineData("Orchestrator", null, null, false)]
    public async Task Only_the_exact_human_shape_with_no_role_and_no_provider_is_coherent(string kind, string? role, string? provider, bool coherent)
    {
        var (_, target, source) = await SeedAsync();
        await ExecuteAsync(
            $"UPDATE events SET \"ActorKind\" = '{kind}', \"ActorAgentRole\" = {(role is null ? "NULL" : $"'{role}'")}, "
            + $"\"ActorAgentProvider\" = {(provider is null ? "NULL" : $"'{provider}'")} WHERE \"Id\" = '{target.EventId.ToString().ToUpperInvariant()}'");

        var (reading, _) = await ReadAsync(source);

        Assert.Equal(coherent ? GitHubObservationTargetReadingKind.Coherent : GitHubObservationTargetReadingKind.Incoherent, reading.Kind);
    }

    [Theory]
    [InlineData("Owner", "replace(hex(zeroblob(150)), '0', 'o')")]
    [InlineData("Repository", "replace(hex(zeroblob(150)), '0', 'r')")]
    [InlineData("CommitSha", "replace(hex(zeroblob(150)), '0', 'a')")]
    [InlineData("BranchName", "replace(hex(zeroblob(2000)), '0', 'b')")]
    public async Task An_oversized_stored_target_field_makes_the_whole_reading_incoherent_behind_a_stored_byte_guard(string column, string expression)
    {
        var (_, target, source) = await SeedAsync();
        await ExecuteAsync($"UPDATE github_observation_targets SET \"{column}\" = {expression} WHERE \"Id\" = '{target.Id.ToString().ToUpperInvariant()}'");
        var before = await FingerprintAsync();

        var (reading, sql) = await ReadAsync(source);

        Assert.Equal(GitHubObservationTargetReadingKind.Incoherent, reading.Kind);
        Assert.Null(reading.Target);
        var targetQuery = Assert.Single(sql, command => command.Contains("github_observation_targets", StringComparison.Ordinal));
        Assert.Contains("CAST", targetQuery, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            System.Text.RegularExpressions.Regex.Matches(targetQuery, "BLOB", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count >= 4,
            "each of the four stored texts (commit, branch, owner, repository) is bounded by its stored bytes");
        Assert.Equal(before, await FingerprintAsync());
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Repository")]
    [InlineData("BranchName")]
    public async Task An_embedded_nul_hides_no_suffix_from_the_stored_byte_guard_of_a_target_field(string column)
    {
        var (_, target, source) = await SeedAsync();
        await ExecuteAsync(
            $"UPDATE github_observation_targets SET \"{column}\" = 'ab' || char(0) || replace(hex(zeroblob(50000)), '0', 'x') WHERE \"Id\" = '{target.Id.ToString().ToUpperInvariant()}'");
        await using (var connection = new SqliteConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var proof = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants and test-chosen identifiers.
            proof.CommandText = $"SELECT length(\"{column}\"), length(CAST(\"{column}\" AS BLOB)) FROM github_observation_targets";
#pragma warning restore CA2100
            await using var reader = await proof.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetInt64(0) < 10, "SQLite text length stops at the NUL");
            Assert.True(reader.GetInt64(1) > 50_000, "the stored bytes include the hidden suffix");
        }

        var (reading, sql) = await ReadAsync(source);

        Assert.Equal(GitHubObservationTargetReadingKind.Incoherent, reading.Kind);
        var targetQuery = Assert.Single(sql, command => command.Contains("github_observation_targets", StringComparison.Ordinal));
        Assert.True(
            System.Text.RegularExpressions.Regex.Matches(targetQuery, "BLOB", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count >= 4);
        Assert.Null(reading.Target);
    }

    [Fact]
    public async Task An_embedded_nul_hides_no_suffix_from_the_stored_byte_guard_of_the_event_payload()
    {
        var (_, _, source) = await SeedAsync();
        await ExecuteAsync(
            "UPDATE events SET \"PayloadJson\" = \"PayloadJson\" || char(0) || replace(hex(zeroblob(50000)), '0', 'x') "
            + $"WHERE \"EventType\" = '{RunEventType.GitHubObservationTargetRecorded}'");
        await using (var connection = new SqliteConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var proof = connection.CreateCommand();
            proof.CommandText =
                "SELECT length(\"PayloadJson\"), length(CAST(\"PayloadJson\" AS BLOB)) FROM events WHERE \"EventType\" = 'run.github_observation_target_recorded'";
            await using var reader = await proof.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetInt64(0) < GitHubObservationTargetEventPayload.MaximumLength);
            Assert.True(reader.GetInt64(1) > GitHubObservationTargetEventPayload.MaximumLength);
        }

        var (reading, sql) = await ReadAsync(source);

        Assert.Equal(GitHubObservationTargetReadingKind.Incoherent, reading.Kind);
        var eventQuery = Assert.Single(sql, command => command.Contains("PayloadJson", StringComparison.Ordinal));
        Assert.Contains("CAST", eventQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BLOB", eventQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_coherent_record_is_still_read_through_the_same_guards()
    {
        var (_, target, source) = await SeedAsync();

        var (reading, sql) = await ReadAsync(source);

        Assert.Equal(GitHubObservationTargetReadingKind.Coherent, reading.Kind);
        Assert.Equal(target.Id, reading.Target!.Id);
        Assert.Equal("octo-cat", reading.Target.Owner);
        Assert.All(sql.Where(command => command.Contains("github_observation_targets", StringComparison.Ordinal)), command =>
            Assert.Contains("BLOB", command, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Genuine_cancellation_still_propagates_and_is_not_an_incoherent_reading()
    {
        var (_, _, source) = await SeedAsync();
        await using var db = _fixture.CreateContext();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => GitHubObservationTargetReader.ReadAsync(db, source.RunId, source, cancelled.Token));
    }

    [Fact]
    public async Task The_production_transaction_already_blocks_an_independent_writer_before_the_no_op_run_update()
    {
        var (rows, _, _) = await SeedAsync();
        await using var first = _fixture.CreateContext();
        await using var transaction = await first.BeginTransactionAsync(CancellationToken.None);
        await using var second = _fixture.CreateContext();
        await second.Database.OpenConnectionAsync();
        await using var write = second.Database.GetDbConnection().CreateCommand();
        write.CommandTimeout = 1;
        write.CommandText = "UPDATE runs SET Lifecycle = Lifecycle WHERE Id = $id";
        var id = write.CreateParameter();
        id.ParameterName = "$id";
        id.Value = rows.Run.Id.ToString().ToUpperInvariant();
        write.Parameters.Add(id);

        var blocked = await Assert.ThrowsAsync<SqliteException>(() => write.ExecuteNonQueryAsync());

        Assert.Equal(5, blocked.SqliteErrorCode);
        await transaction.RollbackAsync();
        Assert.Equal(1, await write.ExecuteNonQueryAsync());
    }
}
