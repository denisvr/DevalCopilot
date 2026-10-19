using System.Data.Common;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Seeds real, dispatched Agent attempts and the collaboration messages that record them as their own attempt through the
/// production <see cref="CollaborationMessage.RecordAgent"/> factory, and edits stored rows directly to damage them. Shared by the two
/// ADR-0054 handler test classes, which deliberately change together.</summary>
internal sealed class AgentAttemptOutputsTestStore(SqliteDatabaseFixture fixture)
{
    public const string Secret = "SECRET-SENTINEL-54d7";

    public static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly string Fingerprint = new('b', 64);

    public SqliteDatabaseFixture Fixture { get; } = fixture;

    public sealed record RecordedCommand(string Text, IReadOnlyList<(string Name, object? Value, SqliteType Type)> Parameters);

    public sealed class RecordingInterceptor : DbCommandInterceptor
    {
        public List<RecordedCommand> Commands { get; } = [];

        private void Record(DbCommand command) => Commands.Add(new RecordedCommand(
            command.CommandText,
            command.Parameters.Cast<SqliteParameter>().Select(parameter => (parameter.ParameterName, parameter.Value, parameter.SqliteType)).ToArray()));

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Throws instead of executing the first command that matches.</summary>
    public sealed class FaultingInterceptor(Func<string, bool> matches) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (matches(command.CommandText))
            {
                throw new InvalidOperationException("injected database fault");
            }

            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Changes the store on its own connection immediately before the content read executes, after the set was proved: the
    /// separate reads a concurrent writer could interleave.</summary>
    public sealed class BeforeContentReadInterceptor(string connectionString, string sql) : DbCommandInterceptor
    {
        public int Applied { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"Summary\"", StringComparison.Ordinal))
            {
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var pragma = connection.CreateCommand();
                pragma.CommandText = "PRAGMA foreign_keys = OFF";
                await pragma.ExecuteNonQueryAsync(cancellationToken);
                await using var change = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only: a fixed statement of the calling test.
                change.CommandText = sql;
#pragma warning restore CA2100
                await change.ExecuteNonQueryAsync(cancellationToken);
                Applied++;
            }

            return result;
        }
    }

    public async Task<Guid> SeedRunAsync()
    {
        await using var dbContext = Fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Output messages", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.Add(project);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", Now);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    /// <summary>A real Agent attempt of the role (a Resolver by default), marked dispatched unless <paramref name="dispatched"/> is false.
    /// A run holds one running attempt, so an earlier running attempt of the same run is first recorded as completed.</summary>
    public async Task<Attempt> SeedAttemptAsync(
        Guid runId, AgentRole role = AgentRole.Resolver, int number = 1, bool dispatched = true)
    {
        var (id, workspace, checkpoint, manifest) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var timeout = TimeSpan.FromMinutes(10);
        var attempt = role switch
        {
            AgentRole.Planner => Attempt.ClaimAgent(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number),
            AgentRole.CriticalReviewer => Attempt.ClaimAgentCriticalReview(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number),
            AgentRole.Resolver => Attempt.ClaimAgentChallengeResolution(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number),
            AgentRole.Implementer => Attempt.ClaimAgentImplementation(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number),
            _ => Attempt.ClaimAgentCodeReview(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number),
        };
        if (dispatched)
        {
            attempt.MarkAgentDispatched(Now.AddSeconds(1));
        }

        await ExecuteAsync($"UPDATE attempts SET Status = 'Completed' WHERE RunId = {runId} AND Status = 'Running'");
        await using var dbContext = Fixture.CreateContext();
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync();
        return attempt;
    }

    /// <summary>The message types the role may author, in a fixed order.</summary>
    public static CollaborationMessageType[] TypesOf(AgentRole role) =>
        [.. CollaborationMessageAuthorPolicy.AllowedMessageTypes(role).Order()];

    public static CollaborationMessage Output(Attempt attempt, CollaborationMessageType type, int index) =>
        CollaborationMessage.RecordAgent(
            attempt,
            Guid.NewGuid(),
            ParticipantIdentity.ForOrchestrator(),
            type,
            type == CollaborationMessageType.Proposal ? null : Guid.NewGuid(),
            $"Recorded summary {index}.",
            CollaborationHistoryFixtureContent.Json(type, index),
            Now.AddSeconds(index + 2).AddTicks(1234567));

    /// <summary>Records <paramref name="count"/> messages of the attempt through the production factory, cycling through the types its
    /// role may author, one save each so their sequences ascend with their index.</summary>
    public async Task<List<CollaborationMessage>> SeedOutputsAsync(Attempt attempt, int count)
    {
        var types = TypesOf(attempt.AgentRole!.Value);
        await using var dbContext = Fixture.CreateContext();
        var messages = new List<CollaborationMessage>();
        for (var index = 0; index < count; index++)
        {
            var message = Output(attempt, types[index % types.Length], index);
            dbContext.CollaborationMessages.Add(message);
            await dbContext.SaveChangesAsync();
            messages.Add(message);
        }

        return messages;
    }

    /// <summary>A message of the run that records no attempt, such as the other collaboration facts of the same run.</summary>
    public async Task<CollaborationMessage> SeedLooseMessageAsync(Guid runId)
    {
        var message = CollaborationMessage.Record(
            Guid.NewGuid(), runId, null, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForOrchestrator(),
            CollaborationMessageType.Proposal, null, "Loose summary.", CollaborationHistoryFixtureContent.Json(CollaborationMessageType.Proposal),
            CollaborationMessageProvenance.ProviderObserved, Now.AddSeconds(90));
        await using var dbContext = Fixture.CreateContext();
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync();
        return message;
    }

    public async Task ExecuteAsync(FormattableString sql)
    {
        await using var dbContext = Fixture.CreateContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    public async Task ExecuteWithoutForeignKeysAsync(FormattableString sql)
    {
        await using var dbContext = Fixture.CreateContext();
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    /// <summary>Applies one stored-column defect to the message at the end of a set, through the same statements for both operations.</summary>
    public async Task DamageAsync(string defect, CollaborationMessage last, Guid otherRun, Attempt attempt)
    {
        switch (defect)
        {
            case "foreign-run":
                await ExecuteAsync($"UPDATE collaboration_messages SET RunId = {otherRun} WHERE Id = {last.Id}");
                break;
            case "unknown-version":
                await ExecuteAsync($"UPDATE collaboration_messages SET ProtocolVersion = '2.0' WHERE Id = {last.Id}");
                break;
            case "unknown-type":
                await ExecuteAsync($"UPDATE collaboration_messages SET Type = {"Overlord" + Secret} WHERE Id = {last.Id}");
                break;
            case "type-not-allowed-for-role":
                await ExecuteAsync($"UPDATE collaboration_messages SET Type = 'ReviewApproval' WHERE Id = {last.Id}");
                break;
            case "human-instruction-type":
                await ExecuteAsync($"UPDATE collaboration_messages SET Type = 'HumanInstruction' WHERE Id = {last.Id}");
                break;
            case "role-mismatch":
                await ExecuteAsync($"UPDATE collaboration_messages SET ActorAgentRole = 'CodeReviewer' WHERE Id = {last.Id}");
                break;
            case "unknown-role":
                await ExecuteAsync($"UPDATE collaboration_messages SET ActorAgentRole = {"Overlord" + Secret} WHERE Id = {last.Id}");
                break;
            case "null-role":
                await ExecuteAsync($"UPDATE collaboration_messages SET ActorAgentRole = NULL WHERE Id = {last.Id}");
                break;
            case "provider-mismatch":
                await ExecuteAsync(
                    $"UPDATE collaboration_messages SET ActorAgentProvider = {(attempt.AgentProvider == AgentProvider.Codex ? "ClaudeCode" : "Codex")} WHERE Id = {last.Id}");
                break;
            case "unknown-provider":
                await ExecuteAsync($"UPDATE collaboration_messages SET ActorAgentProvider = 'Gemini' WHERE Id = {last.Id}");
                break;
            case "actor-kind":
                await ExecuteAsync(
                    $"UPDATE collaboration_messages SET ActorKind = 'Orchestrator', ActorAgentRole = NULL, ActorAgentProvider = NULL WHERE Id = {last.Id}");
                break;
            case "host-constructed":
                await ExecuteAsync($"UPDATE collaboration_messages SET Provenance = 'HostConstructed' WHERE Id = {last.Id}");
                break;
            case "simulated":
                await ExecuteAsync($"UPDATE collaboration_messages SET Provenance = 'Simulated' WHERE Id = {last.Id}");
                break;
            case "unknown-provenance":
                await ExecuteAsync($"UPDATE collaboration_messages SET Provenance = {"Forged" + Secret} WHERE Id = {last.Id}");
                break;
            case "empty-identity":
                await ExecuteAsync($"UPDATE collaboration_messages SET Id = '00000000-0000-0000-0000-000000000000' WHERE Id = {last.Id}");
                break;
            case "zero-sequence":
                await ExecuteWithoutForeignKeysAsync($"UPDATE collaboration_messages SET Sequence = 0 WHERE Id = {last.Id}");
                break;
            case "negative-sequence":
                await ExecuteWithoutForeignKeysAsync($"UPDATE collaboration_messages SET Sequence = -5 WHERE Id = {last.Id}");
                break;
            case "undispatched":
                await ExecuteAsync($"UPDATE attempts SET AgentDispatchedAtUtc = NULL WHERE Id = {attempt.Id}");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(defect), defect, "Unknown test defect.");
        }
    }

    /// <summary>Every stored defect of a message or its attempt that must make the whole set unavailable.</summary>
    public static readonly string[] SetDefects =
    [
        "foreign-run", "unknown-version", "unknown-type", "type-not-allowed-for-role", "human-instruction-type", "role-mismatch",
        "unknown-role", "null-role", "provider-mismatch", "unknown-provider", "actor-kind", "host-constructed", "simulated",
        "unknown-provenance", "empty-identity", "zero-sequence", "negative-sequence", "undispatched",
    ];

    public async Task<string[]> ExplainAsync(RecordedCommand recorded)
    {
        await using var connection = new SqliteConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only: the plan of the exact command EF generated for the calling test, with its own recorded parameters.
        command.CommandText = "EXPLAIN QUERY PLAN " + recorded.Text;
#pragma warning restore CA2100
        foreach (var (name, value, type) in recorded.Parameters)
        {
            command.Parameters.Add(new SqliteParameter(name, type) { Value = value ?? DBNull.Value });
        }

        var details = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            details.Add(reader.GetString(reader.GetOrdinal("detail")));
        }

        return [.. details];
    }

    /// <summary>Every stored row the operations could change, as text, so a read can be proved to write nothing.</summary>
    public async Task<string> SnapshotAsync()
    {
        await using var dbContext = Fixture.CreateContext();
        var attempts = await dbContext.Attempts.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
        var stored = await dbContext.CollaborationMessages.AsNoTracking().OrderBy(row => row.Sequence).ToListAsync();
        var inputs = await dbContext.AttemptInputMessages.AsNoTracking().CountAsync();
        var events = await dbContext.Events.AsNoTracking().CountAsync();
        var artifacts = await dbContext.Artifacts.AsNoTracking().CountAsync();
        return System.Text.Json.JsonSerializer.Serialize(
                attempts.Select(row => new { row.Id, row.Status, row.AttemptNumber, row.CompletedAtUtc, row.AgentDispatchedAtUtc }))
            + System.Text.Json.JsonSerializer.Serialize(
                stored.Select(row => new { row.Id, row.Sequence, row.RunId, row.AttemptId, row.Summary, row.StructuredContentJson, row.OccurredAtUtc }))
            + inputs + "/" + events + "/" + artifacts;
    }
}
