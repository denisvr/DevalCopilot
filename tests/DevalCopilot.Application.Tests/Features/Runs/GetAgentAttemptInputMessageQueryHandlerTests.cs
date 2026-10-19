using System.Data.Common;
using System.Text;
using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputMessage;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputs;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0053 at the query boundary, over real file-backed SQLite with the production migrations: every read proves the whole bounded input
/// set of exactly one Agent attempt again, exactly as the reference inspection does, before it selects the summary and content of exactly
/// the requested member inside the same run through the unchanged ADR-0035/0036 admission. A nonmember is the fixed not-found, an
/// unprovable set or a reread header that disagrees with its admitted reference is unavailable without any part of the set, unavailable
/// content keeps the reference, and nothing is written or substituted.
/// </summary>
public sealed class GetAgentAttemptInputMessageQueryHandlerTests : IAsyncLifetime
{
    private const string Secret = "SECRET-SENTINEL-53c4";
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed record RecordedCommand(string Text, IReadOnlyList<(string Name, object? Value, SqliteType Type)> Parameters);

    private sealed class RecordingInterceptor : DbCommandInterceptor
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
    private sealed class FaultingInterceptor(Func<string, bool> matches) : DbCommandInterceptor
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
    private sealed class BeforeContentReadInterceptor(string connectionString, string sql) : DbCommandInterceptor
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
#pragma warning disable CA2100 // Test-only: a fixed statement of this test class.
                change.CommandText = sql;
#pragma warning restore CA2100
                await change.ExecuteNonQueryAsync(cancellationToken);
                Applied++;
            }

            return result;
        }
    }

    private static readonly CollaborationMessageType[] AllTypes =
    [
        CollaborationMessageType.Proposal,
        CollaborationMessageType.Acceptance,
        CollaborationMessageType.Challenge,
        CollaborationMessageType.Question,
        CollaborationMessageType.Decision,
        CollaborationMessageType.ExecutionReport,
        CollaborationMessageType.ReviewFinding,
        CollaborationMessageType.RevisionResponse,
        CollaborationMessageType.Escalation,
        CollaborationMessageType.ReviewApproval,
        CollaborationMessageType.HumanInstruction,
    ];

    private async Task<Guid> SeedRunAsync()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Input messages", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.Add(project);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", Now);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    /// <summary>A Planner (variant 0) or a Resolver (variant 1) attempt of the run.</summary>
    private async Task<Attempt> SeedAttemptAsync(Guid runId, int variant = 1, int number = 1)
    {
        var (id, workspace, checkpoint, manifest) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var timeout = TimeSpan.FromMinutes(10);
        var attempt = variant == 0
            ? Attempt.ClaimAgent(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number)
            : Attempt.ClaimAgentChallengeResolution(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number);
        await using var dbContext = _fixture.CreateContext();
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync();
        return attempt;
    }

    private static CollaborationMessage Message(Guid runId, CollaborationMessageType type, int index)
    {
        var human = type == CollaborationMessageType.HumanInstruction;
        return CollaborationMessage.Record(
            Guid.NewGuid(), runId, null, CollaborationMessage.ProtocolVersionOne,
            human ? ParticipantIdentity.ForHuman() : ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForOrchestrator(),
            type, type == CollaborationMessageType.Proposal ? null : Guid.NewGuid(), $"Recorded summary {index}.",
            CollaborationHistoryFixtureContent.Json(type, index),
            human ? CollaborationMessageProvenance.HumanSubmitted : CollaborationMessageProvenance.ProviderObserved,
            Now.AddSeconds(index).AddTicks(1234567));
    }

    /// <summary>Records one message per type (cycling through all eleven) and the attempt's input rows referencing them at positions
    /// 0..count-1, and returns the messages in input order with their assigned sequences.</summary>
    private async Task<List<CollaborationMessage>> SeedInputsAsync(Guid runId, Guid attemptId, int count)
    {
        await using var dbContext = _fixture.CreateContext();
        var messages = Enumerable.Range(0, count).Select(index => Message(runId, AllTypes[index % AllTypes.Length], index)).ToList();
        foreach (var message in messages)
        {
            dbContext.CollaborationMessages.Add(message);
            await dbContext.SaveChangesAsync();
        }

        for (var index = 0; index < count; index++)
        {
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attemptId, messages[index].Id, index));
        }

        await dbContext.SaveChangesAsync();
        return messages;
    }

    private async Task<CollaborationMessage> SeedLooseMessageAsync(Guid runId)
    {
        var message = Message(runId, CollaborationMessageType.Proposal, 99);
        await using var dbContext = _fixture.CreateContext();
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync();
        return message;
    }

    private async Task ExecuteAsync(FormattableString sql)
    {
        await using var dbContext = _fixture.CreateContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private async Task ExecuteWithoutForeignKeysAsync(FormattableString sql)
    {
        await using var dbContext = _fixture.CreateContext();
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    // An ordinary string parameter can truncate at an embedded NUL, so the text is stored from UTF-8 bytes cast to TEXT.
    private async Task StoreNulTextAsync(Guid id, string column, string text)
    {
        await using var dbContext = _fixture.CreateContext();
        var bytes = Encoding.UTF8.GetBytes(text);
        if (column == "Summary")
        {
            await dbContext.Database.ExecuteSqlRawAsync("UPDATE collaboration_messages SET Summary = CAST({0} AS TEXT) WHERE Id = {1}", bytes, id);
        }
        else
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE collaboration_messages SET StructuredContentJson = CAST({0} AS TEXT) WHERE Id = {1}", bytes, id);
        }
    }

    private async Task<Result<GetAgentAttemptInputMessageQueryResult>> SendAsync(
        Guid runId, Guid attemptId, Guid messageId, params IInterceptor[] interceptors)
    {
        await using var dbContext = _fixture.CreateContext(interceptors);
        return await new GetAgentAttemptInputMessageQueryHandler(dbContext).HandleAsync(
            new GetAgentAttemptInputMessageQuery(runId, attemptId, messageId), CancellationToken.None);
    }

    private async Task<GetAgentAttemptInputMessageQueryResult> ReadAsync(Guid runId, Guid attemptId, Guid messageId, params IInterceptor[] interceptors)
    {
        var result = await SendAsync(runId, attemptId, messageId, interceptors);
        Assert.True(result.IsSuccess, string.Join(", ", result.IsSuccess ? [] : result.Errors.Select(error => error.Code)));
        return result.Value;
    }

    private static void AssertNotFound(Result<GetAgentAttemptInputMessageQueryResult> result, params Guid[] hidden)
    {
        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempt_input_message.not_found", error.Code);
        Assert.Equal(GetAgentAttemptInputMessageErrors.NotFoundCode, error.Code);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("The requested Agent attempt input message was not found.", error.Description);
        foreach (var value in hidden)
        {
            Assert.DoesNotContain(value.ToString(), error.Description, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertUnavailable(GetAgentAttemptInputMessageQueryResult result, Guid runId, Guid attemptId, Guid messageId)
    {
        Assert.Equal(runId, result.RunId);
        Assert.Equal(attemptId, result.AttemptId);
        Assert.Equal(messageId, result.CollaborationMessageId);
        Assert.Equal(AgentAttemptInputMessageStatus.Unavailable, result.Status);
        Assert.Null(result.Attempt);
        Assert.Null(result.Input);
        Assert.Null(result.Message);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(Secret, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Recorded summary", serialized, StringComparison.Ordinal);
    }

    /// <summary>The header, the selected reference and the message header equal the attempt and the stored message; content is not judged.</summary>
    private static void AssertRecordedHeader(
        GetAgentAttemptInputMessageQueryResult result, Guid runId, Attempt attempt, CollaborationMessage message, int position)
    {
        Assert.Equal(AgentAttemptInputMessageStatus.Recorded, result.Status);
        Assert.Equal(runId, result.RunId);
        Assert.Equal(attempt.Id, result.AttemptId);
        Assert.Equal(message.Id, result.CollaborationMessageId);
        Assert.NotNull(result.Attempt);
        Assert.Equal(attempt.Id, result.Attempt.Id);
        Assert.Equal(attempt.AttemptNumber, result.Attempt.AttemptNumber);
        Assert.Equal(attempt.AgentRole, result.Attempt.Role);
        Assert.Equal(attempt.AgentProvider, result.Attempt.Provider);
        Assert.Equal(attempt.AgentResponseContract, result.Attempt.ResponseContract);
        Assert.NotNull(result.Input);
        Assert.Equal(position, result.Input.Sequence);
        Assert.Equal(message.Id, result.Input.CollaborationMessageId);
        Assert.Equal(message.Type, result.Input.Type);
        Assert.Equal(message.Sequence, result.Input.CollaborationSequence);
        Assert.True(message.OccurredAtUtc.EqualsExact(result.Input.OccurredAtUtc));
        Assert.NotNull(result.Message);
        Assert.Equal(runId, result.Message.RunId);
        Assert.Equal(message.Id, result.Message.Id);
        Assert.Equal(message.Sequence, result.Message.Sequence);
        Assert.True(message.OccurredAtUtc.EqualsExact(result.Message.OccurredAtUtc));
        Assert.Equal("1.0", result.Message.ProtocolVersion);
        Assert.Equal(message.Type.ToString(), result.Message.Type);
    }

    private static void AssertContentUnavailable(GetAgentAttemptInputMessageQueryResult result)
    {
        Assert.NotNull(result.Message);
        Assert.False(result.Message.ContentAvailable);
        Assert.Null(result.Message.Summary);
        Assert.Empty(result.Message.Fields);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(Secret, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_run_a_missing_foreign_or_non_agent_attempt_and_a_nonmember_are_one_fixed_not_found()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var members = await SeedInputsAsync(runId, attempt.Id, 3);
        var loose = await SeedLooseMessageAsync(runId);
        var otherRun = await SeedRunAsync();
        var foreign = await SeedAttemptAsync(otherRun);
        var foreignMembers = await SeedInputsAsync(otherRun, foreign.Id, 2);
        var simulatedRun = await SeedRunAsync();
        var simulated = Attempt.Claim(Guid.NewGuid(), simulatedRun, 1, Now);
        await using (var dbContext = _fixture.CreateContext())
        {
            dbContext.Attempts.Add(simulated);
            await dbContext.SaveChangesAsync();
        }

        var processRun = await SeedRunAsync();
        var process = await SeedAttemptAsync(processRun);
        var processMembers = await SeedInputsAsync(processRun, process.Id, 1);
        await ExecuteAsync($"UPDATE attempts SET Kind = 'Process' WHERE Id = {process.Id}");
        var plannerRun = await SeedRunAsync();
        var planner = await SeedAttemptAsync(plannerRun, 0);
        var plannerRunMessage = await SeedLooseMessageAsync(plannerRun);
        var unknownId = Guid.NewGuid();

        AssertNotFound(await SendAsync(Guid.NewGuid(), attempt.Id, members[0].Id), attempt.Id, members[0].Id);
        AssertNotFound(await SendAsync(runId, unknownId, members[0].Id), unknownId);
        AssertNotFound(await SendAsync(runId, foreign.Id, foreignMembers[0].Id), foreign.Id);
        AssertNotFound(await SendAsync(runId, Guid.Empty, members[0].Id));
        AssertNotFound(await SendAsync(simulatedRun, simulated.Id, members[0].Id), simulated.Id);
        AssertNotFound(await SendAsync(processRun, process.Id, processMembers[0].Id), process.Id);
        // Nonmembers of a fully proved set: a message of the same run that is no input, another attempt's input, an unknown identity, the
        // empty identity, and any message for a coherent empty Planner set.
        AssertNotFound(await SendAsync(runId, attempt.Id, loose.Id), loose.Id);
        AssertNotFound(await SendAsync(runId, attempt.Id, foreignMembers[0].Id), foreignMembers[0].Id);
        AssertNotFound(await SendAsync(runId, attempt.Id, unknownId), unknownId);
        AssertNotFound(await SendAsync(runId, attempt.Id, Guid.Empty));
        AssertNotFound(await SendAsync(plannerRun, planner.Id, plannerRunMessage.Id), plannerRunMessage.Id);

        Assert.Equal(AgentAttemptInputMessageStatus.Recorded, (await ReadAsync(runId, attempt.Id, members[2].Id)).Status);
    }

    [Fact]
    public async Task A_nonmember_reads_no_summary_or_content_of_any_message()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        await SeedInputsAsync(runId, attempt.Id, 3);
        var loose = await SeedLooseMessageAsync(runId);
        var recorder = new RecordingInterceptor();

        AssertNotFound(await SendAsync(runId, attempt.Id, loose.Id, recorder));

        Assert.Equal(3, recorder.Commands.Count);
        Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("Summary", StringComparison.Ordinal));
        Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("StructuredContentJson", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_message_type_of_a_proved_set_is_returned_with_its_admitted_summary_and_fields_in_stored_order()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, number: 4);
        var messages = await SeedInputsAsync(runId, attempt.Id, AllTypes.Length);

        for (var position = 0; position < messages.Count; position++)
        {
            var message = messages[position];
            var result = await ReadAsync(runId, attempt.Id, message.Id);

            AssertRecordedHeader(result, runId, attempt, message, position);
            Assert.Equal(message.Type == CollaborationMessageType.HumanInstruction ? "HumanSubmitted" : "ProviderObserved", result.Message!.Provenance);
            Assert.True(result.Message.ContentAvailable);
            Assert.Equal($"Recorded summary {position}.", result.Message.Summary);
            Assert.Equal(
                CollaborationHistoryFixtureContent.FieldNames[message.Type].Select(name => (name, CollaborationHistoryFixtureContent.Value(name, position))),
                result.Message.Fields.Select(field => (field.Name, field.Value)));
            if (message.Type == CollaborationMessageType.HumanInstruction)
            {
                Assert.Equal(new CollaborationHistoryParticipant(ParticipantKind.Human, null, null), result.Message.Actor);
            }
            else
            {
                Assert.Equal(new CollaborationHistoryParticipant(ParticipantKind.Agent, AgentRole.Planner, AgentProvider.Codex), result.Message.Actor);
            }

            Assert.Equal(new CollaborationHistoryParticipant(ParticipantKind.Orchestrator, null, null), result.Message.Recipient);
            // No other member's text is returned.
            var serialized = JsonSerializer.Serialize(result);
            foreach (var other in Enumerable.Range(0, messages.Count).Where(index => index != position))
            {
                Assert.DoesNotContain($"Recorded summary {other}.", serialized, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(10, 9)]
    [InlineData(11, 10)]
    [InlineData(64, 0)]
    [InlineData(64, 10)]
    [InlineData(64, 63)]
    public async Task Any_member_of_a_set_up_to_sixty_four_is_reachable_including_those_after_the_former_prefix(int count, int position)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, count);

        var result = await ReadAsync(runId, attempt.Id, messages[position].Id);

        AssertRecordedHeader(result, runId, attempt, messages[position], position);
        Assert.True(result.Message!.ContentAvailable);
        Assert.Equal($"Recorded summary {position}.", result.Message.Summary);
    }

    [Fact]
    public async Task A_sixty_fifth_input_makes_every_member_unavailable_and_reads_no_message()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 65);

        foreach (var member in new[] { messages[0], messages[63], messages[64] })
        {
            var recorder = new RecordingInterceptor();
            AssertUnavailable(await ReadAsync(runId, attempt.Id, member.Id, recorder), runId, attempt.Id, member.Id);
            Assert.Equal(2, recorder.Commands.Count);
            Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("collaboration_messages", StringComparison.Ordinal));
        }
    }

    public static TheoryData<string> LastMemberDefects() =>
    [
        "gap", "duplicate-reference", "empty-reference", "dangling-reference", "foreign-reference", "unknown-version", "unknown-type",
        "zero-message-sequence", "malformed-position",
    ];

    [Theory]
    [MemberData(nameof(LastMemberDefects))]
    public async Task Corruption_of_the_last_of_sixty_four_members_makes_the_first_unavailable_before_any_content_is_read(string defect)
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 64);
        Assert.Equal(AgentAttemptInputMessageStatus.Recorded, (await ReadAsync(runId, attempt.Id, messages[0].Id)).Status);
        var last = messages[63];
        switch (defect)
        {
            case "gap":
                await ExecuteAsync($"UPDATE attempt_input_messages SET Sequence = 64 WHERE AttemptId = {attempt.Id} AND Sequence = 63");
                break;
            case "duplicate-reference":
                await ExecuteAsync(
                    $"UPDATE attempt_input_messages SET CollaborationMessageId = {messages[0].Id.ToString().ToLowerInvariant()} WHERE AttemptId = {attempt.Id} AND Sequence = 63");
                break;
            case "empty-reference":
                await ExecuteAsync(
                    $"UPDATE attempt_input_messages SET CollaborationMessageId = '00000000-0000-0000-0000-000000000000' WHERE AttemptId = {attempt.Id} AND Sequence = 63");
                break;
            case "dangling-reference":
                await ExecuteAsync(
                    $"UPDATE attempt_input_messages SET CollaborationMessageId = {Guid.NewGuid().ToString().ToUpperInvariant()} WHERE AttemptId = {attempt.Id} AND Sequence = 63");
                break;
            case "foreign-reference":
                await ExecuteAsync($"UPDATE collaboration_messages SET RunId = {otherRun} WHERE Id = {last.Id}");
                break;
            case "unknown-version":
                await ExecuteAsync($"UPDATE collaboration_messages SET ProtocolVersion = '2.0' WHERE Id = {last.Id}");
                break;
            case "unknown-type":
                await ExecuteAsync($"UPDATE collaboration_messages SET Type = {"Overlord" + Secret} WHERE Id = {last.Id}");
                break;
            case "zero-message-sequence":
                await ExecuteWithoutForeignKeysAsync($"UPDATE collaboration_messages SET Sequence = 0 WHERE Id = {last.Id}");
                break;
            default:
                await ExecuteAsync($"UPDATE attempt_input_messages SET Sequence = 62.5 WHERE AttemptId = {attempt.Id} AND Sequence = 63");
                break;
        }

        var recorder = new RecordingInterceptor();
        AssertUnavailable(await ReadAsync(runId, attempt.Id, messages[0].Id, recorder), runId, attempt.Id, messages[0].Id);
        Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("Summary", StringComparison.Ordinal));
        Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("StructuredContentJson", StringComparison.Ordinal));
        // Neither is the corrupt member itself found or shown: the set is not filtered to make a member appear valid.
        AssertUnavailable(await ReadAsync(runId, attempt.Id, last.Id), runId, attempt.Id, last.Id);
    }

    [Theory]
    [InlineData("AgentRole", "Overlord")]
    [InlineData("AgentProvider", "Gemini")]
    [InlineData("AgentResponseContract", "Nonsense")]
    [InlineData("AgentRole", null)]
    [InlineData("AttemptNumber", "0")]
    public async Task An_attempt_whose_identity_cannot_be_proved_is_unavailable_and_reads_neither_inputs_nor_messages(string column, string? stored)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 2);
        var value = stored is null ? null : column == "AttemptNumber" ? (object)0 : stored + Secret;
        var sql = $"UPDATE attempts SET {column} = @value WHERE Id = @id";
        await using (var dbContext = _fixture.CreateContext())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                sql,
                new SqliteParameter("@value", value ?? DBNull.Value),
                new SqliteParameter("@id", attempt.Id.ToString().ToUpperInvariant()));
        }

        var recorder = new RecordingInterceptor();
        AssertUnavailable(await ReadAsync(runId, attempt.Id, messages[0].Id, recorder), runId, attempt.Id, messages[0].Id);
        Assert.Single(recorder.Commands);
        Assert.Contains("FROM \"attempts\"", recorder.Commands[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_non_planner_attempt_without_inputs_is_unavailable_not_not_found()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var loose = await SeedLooseMessageAsync(runId);

        AssertUnavailable(await ReadAsync(runId, attempt.Id, loose.Id), runId, attempt.Id, loose.Id);
    }

    [Theory]
    [InlineData("Provenance", "Forged")]
    [InlineData("ActorKind", "Wizard")]
    [InlineData("ActorAgentRole", "Overlord")]
    [InlineData("ActorAgentProvider", "Gemini")]
    [InlineData("RecipientKind", "Wizard")]
    [InlineData("RecipientAgentRole", "Overlord")]
    [InlineData("RecipientAgentProvider", "Gemini")]
    public async Task Unrecognized_participants_or_provenance_keep_the_reference_with_content_unavailable(string column, string stored)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 3);
        var sql = $"UPDATE collaboration_messages SET {column} = @value WHERE Id = @id";
        await using (var dbContext = _fixture.CreateContext())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                sql,
                new SqliteParameter("@value", stored + Secret),
                new SqliteParameter("@id", messages[1].Id.ToString().ToUpperInvariant()));
        }

        var result = await ReadAsync(runId, attempt.Id, messages[1].Id);

        AssertRecordedHeader(result, runId, attempt, messages[1], 1);
        AssertContentUnavailable(result);
        Assert.DoesNotContain(stored, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        if (column == "Provenance")
        {
            Assert.Null(result.Message!.Provenance);
        }
        else
        {
            Assert.Null((column.StartsWith("Actor", StringComparison.Ordinal) ? result.Message!.Actor : result.Message!.Recipient).Kind);
        }

        // A sibling's content is unaffected.
        Assert.True((await ReadAsync(runId, attempt.Id, messages[2].Id)).Message!.ContentAvailable);
    }

    [Fact]
    public async Task Incoherent_participants_and_provenance_keep_the_reference_with_content_unavailable()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 5);
        await ExecuteAsync($"UPDATE collaboration_messages SET RecipientKind = 'Agent', RecipientAgentRole = 'Planner', RecipientAgentProvider = 'Codex' WHERE Id = {messages[0].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET ActorKind = 'None', ActorAgentRole = NULL, ActorAgentProvider = NULL WHERE Id = {messages[1].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET ActorKind = 'Human', ActorAgentRole = 'Planner' WHERE Id = {messages[2].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET Provenance = 'HostConstructed' WHERE Id = {messages[3].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET ActorAgentRole = NULL WHERE Id = {messages[4].Id}");

        for (var position = 0; position < messages.Count; position++)
        {
            var result = await ReadAsync(runId, attempt.Id, messages[position].Id);
            AssertRecordedHeader(result, runId, attempt, messages[position], position);
            AssertContentUnavailable(result);
        }
    }

    public static TheoryData<string, string> DefectiveContent() => new()
    {
        { "summary", "" },
        { "summary", "   " },
        { "summary", "The password is hunter2 " + Secret },
        { "summary", "Look at C:\\Users\\x " + Secret },
        { "summary", new string('s', 601) },
        { "summary", string.Concat(Enumerable.Repeat("\U0001F600", 600)) },
        { "summary", new string('s', 1_000_000) + Secret },
        { "json", "not json " + Secret },
        { "json", "{\"scope\":\"" + Secret + "\"}" },
        { "json", "{\"scope\":\"a\",\"implementationSteps\":\"b\",\"risks\":\"c\",\"verificationPlan\":\"d\",\"escalationPoints\":\"e\",\"extra\":\"" + Secret + "\"}" },
        { "json", "{\"scope\":1,\"implementationSteps\":\"b\",\"risks\":\"c\",\"verificationPlan\":\"d\",\"escalationPoints\":\"e\"}" },
        { "json", "{\"scope\":\"api key " + Secret + "\",\"implementationSteps\":\"b\",\"risks\":\"c\",\"verificationPlan\":\"d\",\"escalationPoints\":\"e\"}" },
        { "json", "{\"scope\":\"" + new string('v', 901) + "\",\"implementationSteps\":\"b\",\"risks\":\"c\",\"verificationPlan\":\"d\",\"escalationPoints\":\"e\"}" },
        { "json", "{}" },
        { "json", "" },
        { "json", "{\"scope\":\"" + new string('x', 5000) + "\"}" },
        { "json", "{\"scope\":\"" + new string('x', 2_000_000) + Secret + "\"}" },
    };

    [Theory]
    [MemberData(nameof(DefectiveContent))]
    public async Task Malformed_unsafe_or_oversized_content_keeps_the_reference_with_content_unavailable(string column, string stored)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 3);
        if (column == "summary")
        {
            await ExecuteAsync($"UPDATE collaboration_messages SET Summary = {stored} WHERE Id = {messages[0].Id}");
        }
        else
        {
            await ExecuteAsync($"UPDATE collaboration_messages SET StructuredContentJson = {stored} WHERE Id = {messages[0].Id}");
        }

        var result = await ReadAsync(runId, attempt.Id, messages[0].Id);

        AssertRecordedHeader(result, runId, attempt, messages[0], 0);
        AssertContentUnavailable(result);
        Assert.True(JsonSerializer.Serialize(result).Length < 10_000);
    }

    [Theory]
    [InlineData("Summary")]
    [InlineData("StructuredContentJson")]
    public async Task Oversized_text_hidden_behind_an_embedded_nul_is_not_selected_and_content_stays_unavailable(string column)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 2);
        var valid = column == "Summary" ? "Safe summary" : CollaborationHistoryFixtureContent.Json(CollaborationMessageType.Proposal);
        await StoreNulTextAsync(messages[0].Id, column, valid + "\0" + new string('x', 2_000_000) + Secret);
        var recorder = new RecordingInterceptor();

        var result = await ReadAsync(runId, attempt.Id, messages[0].Id, recorder);

        AssertRecordedHeader(result, runId, attempt, messages[0], 0);
        AssertContentUnavailable(result);

        // The content statement itself, replayed with its own parameters, returns the damaged column as null rather than its bytes.
        var content = Assert.Single(recorder.Commands, command => command.Text.Contains("\"Summary\"", StringComparison.Ordinal));
        await using var connection = new SqliteConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only: the statement is the handler's own, replayed with its own parameters.
        command.CommandText = content.Text;
#pragma warning restore CA2100
        foreach (var (name, value, type) in content.Parameters)
        {
            command.Parameters.Add(new SqliteParameter(name, type) { Value = value ?? DBNull.Value });
        }

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(column == "Summary" ? 12 : 13));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task Sequences_beyond_a_browser_number_and_seven_digit_times_are_returned_exactly()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 2);
        Assert.Equal(1234567, messages[1].OccurredAtUtc.Ticks % TimeSpan.TicksPerSecond);

        foreach (var sequence in new[] { 9007199254740993L, 9223372036854775806L })
        {
            await ExecuteWithoutForeignKeysAsync($"UPDATE collaboration_messages SET Sequence = {sequence} WHERE Id = {messages[1].Id}");

            var result = await ReadAsync(runId, attempt.Id, messages[1].Id);

            Assert.Equal(AgentAttemptInputMessageStatus.Recorded, result.Status);
            Assert.Equal(sequence, result.Input!.CollaborationSequence);
            Assert.Equal(sequence, result.Message!.Sequence);
            Assert.True(messages[1].OccurredAtUtc.EqualsExact(result.Input.OccurredAtUtc));
            Assert.True(messages[1].OccurredAtUtc.EqualsExact(result.Message.OccurredAtUtc));
            Assert.True(result.Message.ContentAvailable);
        }
    }

    [Fact]
    public async Task Malformed_identity_or_timestamp_storage_keeps_the_fault_pipeline()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 2);
        await ExecuteAsync($"UPDATE collaboration_messages SET OccurredAtUtc = 'not-a-time' WHERE Id = {messages[1].Id}");

        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(runId, attempt.Id, messages[0].Id));

        var other = await SeedRunAsync();
        var second = await SeedAttemptAsync(other);
        var secondMessages = await SeedInputsAsync(other, second.Id, 2);
        await ExecuteAsync($"UPDATE attempt_input_messages SET CollaborationMessageId = 'not-a-guid' WHERE AttemptId = {second.Id} AND Sequence = 1");

        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(other, second.Id, secondMessages[0].Id));
    }

    public static TheoryData<string, string> RereadDisagreements() => new()
    {
        { "type", "UPDATE collaboration_messages SET Type = 'Challenge' WHERE Id = '{0}'" },
        { "unknown-type", "UPDATE collaboration_messages SET Type = 'Overlord" + Secret + "' WHERE Id = '{0}'" },
        { "version", "UPDATE collaboration_messages SET ProtocolVersion = '2.0' WHERE Id = '{0}'" },
        { "sequence", "UPDATE collaboration_messages SET Sequence = Sequence + 1000 WHERE Id = '{0}'" },
        { "time", "UPDATE collaboration_messages SET OccurredAtUtc = '2030-01-01 00:00:00+00:00' WHERE Id = '{0}'" },
        { "run", "UPDATE collaboration_messages SET RunId = '{1}' WHERE Id = '{0}'" },
        { "deleted", "DELETE FROM collaboration_messages WHERE Id = '{0}'" },
    };

    [Theory]
    [MemberData(nameof(RereadDisagreements))]
    public async Task A_reread_header_that_disagrees_with_the_admitted_reference_is_unavailable_never_a_substitute(string _, string template)
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 3);
        var changer = new BeforeContentReadInterceptor(
            _fixture.ConnectionString, string.Format(System.Globalization.CultureInfo.InvariantCulture, template,
                messages[0].Id.ToString().ToUpperInvariant(), otherRun.ToString().ToUpperInvariant()));

        var result = await ReadAsync(runId, attempt.Id, messages[0].Id, changer);

        Assert.Equal(1, changer.Applied);
        AssertUnavailable(result, runId, attempt.Id, messages[0].Id);
    }

    [Fact]
    public async Task Content_changed_between_the_reads_is_shown_as_reread_because_separate_reads_claim_no_snapshot()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 2);
        var changer = new BeforeContentReadInterceptor(
            _fixture.ConnectionString,
            $"UPDATE collaboration_messages SET Summary = 'Reread summary.' WHERE Id = '{messages[1].Id.ToString().ToUpperInvariant()}'");

        var result = await ReadAsync(runId, attempt.Id, messages[1].Id, changer);

        AssertRecordedHeader(result, runId, attempt, messages[1], 1);
        Assert.Equal("Reread summary.", result.Message!.Summary);
    }

    [Theory]
    [InlineData("attempts")]
    [InlineData("attempt_input_messages")]
    [InlineData("metadata")]
    [InlineData("content")]
    public async Task A_database_fault_propagates_and_is_not_masked_as_unavailable(string read)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 2);
        Func<string, bool> matches = read switch
        {
            "metadata" => text => text.Contains("FROM \"collaboration_messages\"", StringComparison.Ordinal) && !text.Contains("\"Summary\"", StringComparison.Ordinal),
            "content" => text => text.Contains("\"Summary\"", StringComparison.Ordinal),
            _ => text => text.Contains($"FROM \"{read}\"", StringComparison.Ordinal),
        };

        var fault = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SendAsync(runId, attempt.Id, messages[0].Id, new FaultingInterceptor(matches)));
        Assert.Equal("injected database fault", fault.Message);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 2);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await using var dbContext = _fixture.CreateContext();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GetAgentAttemptInputMessageQueryHandler(dbContext).HandleAsync(
            new GetAgentAttemptInputMessageQuery(runId, attempt.Id, messages[0].Id), cancellation.Token));
    }

    [Fact]
    public async Task The_read_is_the_unchanged_set_proof_then_one_guarded_select_of_exactly_that_member()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 12);
        var proof = new RecordingInterceptor();
        var recorder = new RecordingInterceptor();

        await using (var dbContext = _fixture.CreateContext(proof))
        {
            await new GetAgentAttemptInputsQueryHandler(dbContext).HandleAsync(new GetAgentAttemptInputsQuery(runId, attempt.Id), CancellationToken.None);
        }

        var result = await SendAsync(runId, attempt.Id, messages[11].Id, recorder);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentAttemptInputMessageStatus.Recorded, result.Value.Status);
        Assert.Equal(4, recorder.Commands.Count);
        // The first three statements are exactly the reference inspection's proof, which never touches text.
        Assert.Equal(proof.Commands.Select(command => command.Text), recorder.Commands.Take(3).Select(command => command.Text));
        Assert.All(recorder.Commands.Take(3), command => Assert.DoesNotContain("Summary", command.Text, StringComparison.Ordinal));
        Assert.All(recorder.Commands, command =>
        {
            Assert.StartsWith("SELECT", command.Text.TrimStart(), StringComparison.OrdinalIgnoreCase);
            foreach (var forbidden in new[] { "OFFSET", "COUNT(", "JOIN", "UPDATE", "INSERT", "DELETE", "InReplyToMessageId", "\"AttemptId\" IN" })
            {
                Assert.DoesNotContain(forbidden, command.Text, StringComparison.OrdinalIgnoreCase);
            }
        });

        var content = recorder.Commands[3];
        Assert.Contains("FROM \"collaboration_messages\"", content.Text, StringComparison.Ordinal);
        Assert.Contains("\"RunId\" =", content.Text, StringComparison.Ordinal);
        Assert.Contains("\"Id\" =", content.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", content.Text, StringComparison.Ordinal);
        Assert.Matches(@"(?i)length\(CAST\([^)]*""Summary""\s+AS\s+BLOB\)\)\s*<=\s*1800(?!\d)", content.Text);
        Assert.Matches(@"(?i)length\(CAST\([^)]*""StructuredContentJson""\s+AS\s+BLOB\)\)\s*<=\s*15000(?!\d)", content.Text);
        Assert.DoesNotContain("\"AttemptId\"", content.Text, StringComparison.Ordinal);
        // Exactly the requested member is a parameter; no other message identity is.
        var identities = content.Parameters.Select(parameter => parameter.Value?.ToString() ?? string.Empty).ToArray();
        Assert.Contains(identities, value => string.Equals(value, messages[11].Id.ToString(), StringComparison.OrdinalIgnoreCase));
        foreach (var other in messages.Take(11))
        {
            Assert.DoesNotContain(identities, value => string.Equals(value, other.Id.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        var plan = await ExplainAsync(content);
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN", StringComparison.Ordinal));
        Assert.Contains(plan, detail => detail.StartsWith("SEARCH", StringComparison.Ordinal));
    }

    private async Task<string[]> ExplainAsync(RecordedCommand recorded)
    {
        await using var connection = new SqliteConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only: the plan of the exact command EF generated for this test, with its own recorded parameters.
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

    [Fact]
    public async Task Exact_ownership_holds_despite_newer_runs_attempts_and_messages()
    {
        var olderRun = await SeedRunAsync();
        var older = await SeedAttemptAsync(olderRun, number: 1);
        await ExecuteAsync($"UPDATE attempts SET Status = 'Completed' WHERE Id = {older.Id}");
        var olderMessages = await SeedInputsAsync(olderRun, older.Id, 3);
        var newerSameRun = await SeedAttemptAsync(olderRun, number: 2);
        var newerSameRunMessages = await SeedInputsAsync(olderRun, newerSameRun.Id, 2);
        var newerRun = await SeedRunAsync();
        var newer = await SeedAttemptAsync(newerRun);
        var newerMessages = await SeedInputsAsync(newerRun, newer.Id, 2);

        var result = await ReadAsync(olderRun, older.Id, olderMessages[2].Id);

        AssertRecordedHeader(result, olderRun, older, olderMessages[2], 2);
        AssertNotFound(await SendAsync(olderRun, older.Id, newerSameRunMessages[0].Id));
        AssertNotFound(await SendAsync(olderRun, older.Id, newerMessages[0].Id));
        AssertNotFound(await SendAsync(newerRun, older.Id, olderMessages[0].Id));
        AssertRecordedHeader(await ReadAsync(newerRun, newer.Id, newerMessages[1].Id), newerRun, newer, newerMessages[1], 1);
    }

    [Fact]
    public async Task Reading_changes_no_stored_row()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 11);

        async Task<string> SnapshotAsync()
        {
            await using var dbContext = _fixture.CreateContext();
            var attempts = await dbContext.Attempts.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
            var stored = await dbContext.CollaborationMessages.AsNoTracking().OrderBy(row => row.Sequence).ToListAsync();
            var inputs = await dbContext.AttemptInputMessages.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
            var events = await dbContext.Events.AsNoTracking().CountAsync();
            var artifacts = await dbContext.Artifacts.AsNoTracking().CountAsync();
            return JsonSerializer.Serialize(attempts.Select(row => new { row.Id, row.Status, row.AttemptNumber, row.CompletedAtUtc }))
                + JsonSerializer.Serialize(stored.Select(row => new { row.Id, row.Sequence, row.AttemptId, row.Summary, row.StructuredContentJson, row.OccurredAtUtc }))
                + JsonSerializer.Serialize(inputs.Select(row => new { row.Id, row.AttemptId, row.CollaborationMessageId, row.Sequence }))
                + events + "/" + artifacts;
        }

        var before = await SnapshotAsync();
        var recorder = new RecordingInterceptor();
        await SendAsync(runId, attempt.Id, messages[10].Id, recorder);
        await SendAsync(runId, attempt.Id, Guid.NewGuid(), recorder);
        await SendAsync(runId, Guid.NewGuid(), messages[0].Id, recorder);

        Assert.Equal(before, await SnapshotAsync());
        Assert.All(recorder.Commands, command => Assert.StartsWith("SELECT", command.Text.TrimStart(), StringComparison.OrdinalIgnoreCase));
    }
}
