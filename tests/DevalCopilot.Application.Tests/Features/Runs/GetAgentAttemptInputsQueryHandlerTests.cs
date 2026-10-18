using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0051 at the query boundary, over real file-backed SQLite with the production migrations: exactly one Agent attempt of the run is
/// resolved through the shared bounded scalar identity projection, at most 65 of its input rows and at most the 64 messages they reference
/// are read as scalars, the whole inspected set is judged before any prefix is shown, malformed storage is refused rather than converted,
/// and nothing is written or substituted.
/// </summary>
public sealed class GetAgentAttemptInputsQueryHandlerTests : IAsyncLifetime
{
    private const string Secret = "SECRET-SENTINEL-51a7";
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

    private sealed class FaultingInterceptor(string table) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains($"FROM \"{table}\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("injected database fault");
            }

            return ValueTask.FromResult(result);
        }
    }

    // The five supported role/provider/contract combinations the product launches.
    private static readonly AgentRole[] Roles =
        [AgentRole.Planner, AgentRole.CriticalReviewer, AgentRole.Resolver, AgentRole.Implementer, AgentRole.CodeReviewer];

    private static readonly CollaborationMessageType[] InputTypes =
    [
        CollaborationMessageType.Proposal,
        CollaborationMessageType.Challenge,
        CollaborationMessageType.ExecutionReport,
        CollaborationMessageType.ReviewFinding,
        CollaborationMessageType.Decision,
        CollaborationMessageType.Acceptance,
        CollaborationMessageType.Question,
        CollaborationMessageType.RevisionResponse,
        CollaborationMessageType.Escalation,
        CollaborationMessageType.ReviewApproval,
    ];

    private async Task<Guid> SeedRunAsync()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Inputs", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.Add(project);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", Now);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    private static Attempt Claim(Guid runId, int variant, int number, int? maxTurns = null)
    {
        var (id, workspace, checkpoint, manifest) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var timeout = TimeSpan.FromMinutes(10);
        return variant switch
        {
            0 => Attempt.ClaimAgent(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number),
            1 => Attempt.ClaimAgentCriticalReview(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number),
            2 => Attempt.ClaimAgentChallengeResolution(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number),
            3 => Attempt.ClaimAgentImplementationWithAssignment(
                id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, "sonnet", "high",
                AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ImplementationV2, number, maxTurns),
            _ => Attempt.ClaimAgentCodeReview(id, runId, number, workspace, checkpoint, Fingerprint, manifest, timeout, 1024, 2048, Now, number),
        };
    }

    private async Task<Attempt> SeedAttemptAsync(Guid runId, int variant = 0, int number = 1, int? maxTurns = null)
    {
        var attempt = Claim(runId, variant, number, maxTurns);
        await using var dbContext = _fixture.CreateContext();
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync();
        return attempt;
    }

    private static CollaborationMessage Message(Guid runId, CollaborationMessageType type, int index) => CollaborationMessage.Record(
        Guid.NewGuid(), runId, null, CollaborationMessage.ProtocolVersionOne,
        ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForOrchestrator(),
        type, type == CollaborationMessageType.Proposal ? null : Guid.NewGuid(), $"Recorded summary {index}.",
        CollaborationHistoryFixtureContent.Json(type, index), CollaborationMessageProvenance.ProviderObserved, Now.AddSeconds(index));

    /// <summary>Records <paramref name="count"/> messages of the run and the attempt's input rows referencing them at positions 0..count-1,
    /// and returns the messages in input order with their assigned sequences.</summary>
    private async Task<List<CollaborationMessage>> SeedInputsAsync(Guid runId, Guid attemptId, int count, Guid? messageRunId = null)
    {
        await using var dbContext = _fixture.CreateContext();
        var messages = Enumerable.Range(0, count).Select(index => Message(messageRunId ?? runId, InputTypes[index % InputTypes.Length], index)).ToList();
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

    private async Task ExecuteAsync(FormattableString sql)
    {
        await using var dbContext = _fixture.CreateContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    /// <summary>Writes damaged rows no constraint would accept, on one explicitly opened connection with foreign keys off.</summary>
    private async Task ExecuteWithoutForeignKeysAsync(FormattableString sql)
    {
        await using var dbContext = _fixture.CreateContext();
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private Task SetPositionAsync(Guid attemptId, int position, object? stored) =>
        ExecuteAsync($"UPDATE attempt_input_messages SET Sequence = {stored} WHERE AttemptId = {attemptId} AND Sequence = {position}");

    private async Task<Result<GetAgentAttemptInputsQueryResult>> SendAsync(Guid runId, Guid attemptId, params IInterceptor[] interceptors)
    {
        await using var dbContext = _fixture.CreateContext(interceptors);
        return await new GetAgentAttemptInputsQueryHandler(dbContext).HandleAsync(
            new GetAgentAttemptInputsQuery(runId, attemptId), CancellationToken.None);
    }

    private async Task<GetAgentAttemptInputsQueryResult> ReadAsync(Guid runId, Guid attemptId)
    {
        var result = await SendAsync(runId, attemptId);
        Assert.True(result.IsSuccess, string.Join(", ", result.IsSuccess ? [] : result.Errors.Select(error => error.Code)));
        return result.Value;
    }

    private static void AssertUnavailable(GetAgentAttemptInputsQueryResult result, Guid runId, Guid attemptId)
    {
        Assert.Equal(runId, result.RunId);
        Assert.Equal(attemptId, result.AttemptId);
        Assert.Equal(AgentAttemptInputsStatus.Unavailable, result.Status);
        Assert.Null(result.Attempt);
        Assert.Empty(result.InputMessages);
        Assert.Null(result.InputMessageTotalCount);
        Assert.False(result.InputMessagesOmitted);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    private static void AssertHeader(Attempt expected, AgentAttemptInputsHeader? header)
    {
        Assert.NotNull(header);
        Assert.Equal(expected.Id, header.Id);
        Assert.Equal(expected.AttemptNumber, header.AttemptNumber);
        Assert.Equal(expected.AgentRole, header.Role);
        Assert.Equal(expected.AgentProvider, header.Provider);
        Assert.Equal(expected.AgentResponseContract, header.ResponseContract);
    }

    private static void AssertReferences(IReadOnlyList<CollaborationMessage> messages, IReadOnlyList<AgentAttemptInputReference> references)
    {
        Assert.Equal(Math.Min(messages.Count, 10), references.Count);
        for (var index = 0; index < references.Count; index++)
        {
            Assert.Equal(index, references[index].Sequence);
            Assert.Equal(messages[index].Id, references[index].CollaborationMessageId);
            Assert.Equal(messages[index].Type, references[index].Type);
            Assert.Equal(messages[index].Sequence, references[index].CollaborationSequence);
            Assert.Equal(messages[index].OccurredAtUtc, references[index].OccurredAtUtc);
        }
    }

    [Fact]
    public async Task An_unknown_run_and_a_missing_foreign_or_non_agent_attempt_are_one_fixed_not_found()
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        var foreign = await SeedAttemptAsync(otherRun);
        var simulatedRun = await SeedRunAsync();
        var simulated = Attempt.Claim(Guid.NewGuid(), simulatedRun, 1, Now);
        await using (var dbContext = _fixture.CreateContext())
        {
            dbContext.Attempts.Add(simulated);
            await dbContext.SaveChangesAsync();
        }

        var processRun = await SeedRunAsync();
        var process = await SeedAttemptAsync(processRun);
        await ExecuteAsync($"UPDATE attempts SET Kind = 'Process' WHERE Id = {process.Id}");
        var unknownKindRun = await SeedRunAsync();
        var unknownKind = await SeedAttemptAsync(unknownKindRun);
        await ExecuteAsync($"UPDATE attempts SET Kind = {"Wizard" + Secret} WHERE Id = {unknownKind.Id}");

        foreach (var result in new[]
        {
            await SendAsync(Guid.NewGuid(), foreign.Id),
            await SendAsync(runId, Guid.NewGuid()),
            await SendAsync(runId, foreign.Id),
            await SendAsync(runId, Guid.Empty),
            await SendAsync(simulatedRun, simulated.Id),
            await SendAsync(processRun, process.Id),
            await SendAsync(unknownKindRun, unknownKind.Id),
        })
        {
            Assert.True(result.IsFailure);
            var error = Assert.Single(result.Errors);
            Assert.Equal(GetAgentAttemptInputsErrors.NotFoundCode, error.Code);
            Assert.Equal("agent_attempt_inputs.not_found", error.Code);
            Assert.Equal(ErrorType.NotFound, error.Type);
            Assert.DoesNotContain(Secret, error.Description, StringComparison.Ordinal);
            Assert.DoesNotContain(foreign.Id.ToString(), error.Description, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(AgentAttemptInputsStatus.Empty, (await ReadAsync(otherRun, foreign.Id)).Status);
    }

    [Fact]
    public async Task A_coherent_planner_attempt_with_no_inputs_is_empty_with_its_immutable_header()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 0, number: 3);
        await ExecuteAsync($"UPDATE attempts SET Status = 'Failed', CompletedAtUtc = '2026-10-18 09:05:00+00:00' WHERE Id = {attempt.Id}");

        var result = await ReadAsync(runId, attempt.Id);

        Assert.Equal(runId, result.RunId);
        Assert.Equal(attempt.Id, result.AttemptId);
        Assert.Equal(AgentAttemptInputsStatus.Empty, result.Status);
        AssertHeader(attempt, result.Attempt);
        Assert.Equal(AgentRole.Planner, result.Attempt!.Role);
        Assert.Equal(3, result.Attempt.AttemptNumber);
        Assert.Empty(result.InputMessages);
        Assert.Equal(0, result.InputMessageTotalCount);
        Assert.False(result.InputMessagesOmitted);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Zero_inputs_for_any_role_other_than_planner_are_unavailable_not_empty(int variant)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, variant);

        AssertUnavailable(await ReadAsync(runId, attempt.Id), runId, attempt.Id);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    [InlineData(4, 4)]
    public async Task A_coherent_set_is_recorded_in_stored_order_with_exact_references_for_every_supported_role(int variant, int count)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, variant, number: 2);
        var messages = await SeedInputsAsync(runId, attempt.Id, count);

        var result = await ReadAsync(runId, attempt.Id);

        Assert.Equal(AgentAttemptInputsStatus.Recorded, result.Status);
        AssertHeader(attempt, result.Attempt);
        Assert.Equal(Roles[variant], result.Attempt!.Role);
        Assert.Equal(count, result.InputMessageTotalCount);
        Assert.False(result.InputMessagesOmitted);
        AssertReferences(messages, result.InputMessages);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("Recorded summary", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Recorded scope text", serialized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(11, true)]
    [InlineData(64, true)]
    public async Task At_most_ten_references_are_shown_and_omission_is_exact_up_to_sixty_four(int count, bool omitted)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 2);
        var messages = await SeedInputsAsync(runId, attempt.Id, count);

        var result = await ReadAsync(runId, attempt.Id);

        Assert.Equal(AgentAttemptInputsStatus.Recorded, result.Status);
        Assert.Equal(count, result.InputMessageTotalCount);
        Assert.Equal(omitted, result.InputMessagesOmitted);
        Assert.Equal(10, result.InputMessages.Count);
        AssertReferences(messages, result.InputMessages);
    }

    [Fact]
    public async Task A_sixty_fifth_input_makes_the_inspection_unavailable_without_a_count_or_a_partial_list()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 2);
        await SeedInputsAsync(runId, attempt.Id, 65);
        var recorder = new RecordingInterceptor();

        var result = await SendAsync(runId, attempt.Id, recorder);

        Assert.True(result.IsSuccess);
        AssertUnavailable(result.Value, runId, attempt.Id);
        // The refusal needs no message read.
        Assert.Equal(2, recorder.Commands.Count);
        Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("FROM \"collaboration_messages\"", StringComparison.Ordinal));
    }

    public static TheoryData<string> SetDefects() =>
    [
        "gap", "starts-at-one", "duplicate-position", "duplicate-reference", "empty-reference", "dangling-reference", "foreign-reference",
        "unknown-version", "unknown-type", "zero-message-sequence", "negative-message-sequence",
    ];

    [Theory]
    [MemberData(nameof(SetDefects))]
    public async Task Any_incoherence_anywhere_in_the_inspected_set_even_after_the_shown_prefix_is_unavailable(string defect)
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 2);
        var messages = await SeedInputsAsync(runId, attempt.Id, 12);
        Assert.Equal(AgentAttemptInputsStatus.Recorded, (await ReadAsync(runId, attempt.Id)).Status);
        var last = messages[11];
        switch (defect)
        {
            case "gap":
                await SetPositionAsync(attempt.Id, 11, 12);
                break;
            case "starts-at-one":
                for (var position = 11; position >= 0; position--)
                {
                    await SetPositionAsync(attempt.Id, position, position + 1);
                }

                break;
            case "duplicate-position":
                // Only a damaged store can hold this: the unique (AttemptId, Sequence) index is removed first.
                await ExecuteAsync($"DROP INDEX IX_attempt_input_messages_AttemptId_Sequence");
                await SetPositionAsync(attempt.Id, 11, 10);
                break;
            case "duplicate-reference":
                // The same identity in a different text form passes the unique text index but is the same message twice.
                await ExecuteAsync(
                    $"UPDATE attempt_input_messages SET CollaborationMessageId = {messages[0].Id.ToString().ToLowerInvariant()} WHERE AttemptId = {attempt.Id} AND Sequence = 11");
                break;
            case "empty-reference":
                await ExecuteAsync(
                    $"UPDATE attempt_input_messages SET CollaborationMessageId = '00000000-0000-0000-0000-000000000000' WHERE AttemptId = {attempt.Id} AND Sequence = 11");
                break;
            case "dangling-reference":
                await ExecuteAsync(
                    $"UPDATE attempt_input_messages SET CollaborationMessageId = {Guid.NewGuid().ToString().ToUpperInvariant()} WHERE AttemptId = {attempt.Id} AND Sequence = 11");
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
                await ExecuteWithoutForeignKeysAsync($"UPDATE collaboration_messages SET Sequence = -9 WHERE Id = {last.Id}");
                break;
        }

        AssertUnavailable(await ReadAsync(runId, attempt.Id), runId, attempt.Id);
    }

    public static TheoryData<string, object, string> MalformedPositions() => new()
    {
        { "text", "abc", "text" },
        { "numeric-prefixed text", "11x", "text" },
        { "empty text", string.Empty, "text" },
        { "real", 11.5d, "real" },
        { "blob", new byte[] { 0x31, 0x31 }, "blob" },
        { "integer past the bound", 1L << 40, "integer" },
        { "negative integer", -1L, "integer" },
        { "huge text", new string('1', 100_000) + "x", "text" },
        { "digits past an integer become real", new string('1', 400), "real" },
    };

    [Theory]
    [MemberData(nameof(MalformedPositions))]
    public async Task A_malformed_stored_position_is_refused_never_converted_into_a_valid_one(string _, object stored, string storageClass)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 2);
        var messages = await SeedInputsAsync(runId, attempt.Id, 12);
        await SetPositionAsync(attempt.Id, 11, stored);
        Assert.Equal(storageClass, await PositionStorageClassAsync(messages[11].Id));

        AssertUnavailable(await ReadAsync(runId, attempt.Id), runId, attempt.Id);
    }

    [Fact]
    public async Task Text_that_sqlite_affinity_stores_as_an_integer_is_an_integer_position()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 2);
        var messages = await SeedInputsAsync(runId, attempt.Id, 12);
        await ExecuteAsync($"UPDATE attempt_input_messages SET Sequence = '11' WHERE AttemptId = {attempt.Id} AND Sequence = 11");
        Assert.Equal("integer", await PositionStorageClassAsync(messages[11].Id));

        var result = await ReadAsync(runId, attempt.Id);

        Assert.Equal(AgentAttemptInputsStatus.Recorded, result.Status);
        Assert.Equal(12, result.InputMessageTotalCount);
    }

    private async Task<string> PositionStorageClassAsync(Guid messageId)
    {
        await using var connection = new SqliteConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(Sequence) FROM attempt_input_messages WHERE CollaborationMessageId = $message";
        command.Parameters.AddWithValue("$message", messageId.ToString().ToUpperInvariant());
        return (string)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task The_message_sequence_is_an_integer_primary_key_so_it_cannot_hold_converted_storage()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 2);
        var messages = await SeedInputsAsync(runId, attempt.Id, 1);

        foreach (var stored in new object[] { "abc", 1.5d, new byte[] { 0x31 } })
        {
            await Assert.ThrowsAsync<SqliteException>(() =>
                ExecuteWithoutForeignKeysAsync($"UPDATE collaboration_messages SET Sequence = {stored} WHERE Id = {messages[0].Id}"));
        }
    }

    [Fact]
    public async Task Message_sequences_beyond_a_browser_number_are_returned_exactly()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 2);
        var messages = await SeedInputsAsync(runId, attempt.Id, 2);

        foreach (var sequence in new[] { 9007199254740993L, 9223372036854775806L })
        {
            await ExecuteWithoutForeignKeysAsync($"UPDATE collaboration_messages SET Sequence = {sequence} WHERE Id = {messages[1].Id}");

            var result = await ReadAsync(runId, attempt.Id);

            Assert.Equal(AgentAttemptInputsStatus.Recorded, result.Status);
            Assert.Equal(sequence, result.InputMessages[1].CollaborationSequence);
            Assert.Equal(sequence.ToString(CultureInfo.InvariantCulture), result.InputMessages[1].CollaborationSequence.ToString(CultureInfo.InvariantCulture));
        }
    }

    [Theory]
    [InlineData("AgentProvider", "ClaudeCode")]
    [InlineData("AgentResponseContract", "ChallengeResolution")]
    [InlineData("Status", "Exploded")]
    [InlineData("AgentRole", "Overlord")]
    [InlineData("AgentProvider", "Gemini")]
    [InlineData("AgentResponseContract", "Nonsense")]
    [InlineData("AgentPermissionProfile", "Nonsense")]
    [InlineData("AgentRole", null)]
    [InlineData("AgentProvider", null)]
    [InlineData("AgentResponseContract", null)]
    [InlineData("AttemptNumber", "0")]
    [InlineData("AgentRequestedModel", "")]
    public async Task An_attempt_whose_identity_cannot_be_proved_is_unavailable_and_its_inputs_are_never_read(string column, string? stored)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 0);
        await SeedInputsAsync(runId, attempt.Id, 2);
        var value = stored is null ? null : column == "AttemptNumber" ? (object)0 : stored + (stored.Length == 0 ? string.Empty : Secret);
        var sql = $"UPDATE attempts SET {column} = @value WHERE Id = @id";
        await using (var dbContext = _fixture.CreateContext())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                sql, new SqliteParameter("@value", value ?? DBNull.Value), new SqliteParameter("@id", attempt.Id.ToString().ToUpperInvariant()));
        }

        var recorder = new RecordingInterceptor();
        var result = await SendAsync(runId, attempt.Id, recorder);

        Assert.True(result.IsSuccess);
        AssertUnavailable(result.Value, runId, attempt.Id);
        Assert.Single(recorder.Commands);
        Assert.Contains("FROM \"attempts\"", recorder.Commands[0].Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AgentRequestedModel")]
    [InlineData("AgentObservedModel")]
    [InlineData("AgentRequestedEffort")]
    [InlineData("AgentObservedEffort")]
    [InlineData("AgentAdapterContractVersion")]
    public async Task Assignment_strings_keep_the_exact_bound_and_huge_or_nul_hidden_storage_is_rejected_not_read_as_null(string column)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 0);

        await StoreTextAsync(attempt.Id, column, Encoding.UTF8.GetBytes(new string('x', 128)));
        Assert.Equal(AgentAttemptInputsStatus.Empty, (await ReadAsync(runId, attempt.Id)).Status);
        await StoreTextAsync(attempt.Id, column, Encoding.UTF8.GetBytes(new string('x', 129)));
        AssertUnavailable(await ReadAsync(runId, attempt.Id), runId, attempt.Id);
        await StoreTextAsync(attempt.Id, column, Encoding.UTF8.GetBytes("a\0" + new string('b', 200)));
        AssertUnavailable(await ReadAsync(runId, attempt.Id), runId, attempt.Id);
        await StoreTextAsync(attempt.Id, column, Encoding.UTF8.GetBytes("ok\0" + new string('x', 3_000_000)));
        AssertUnavailable(await ReadAsync(runId, attempt.Id), runId, attempt.Id);
        await ExecuteAsync($"UPDATE attempts SET AgentRequestedModel = NULL, AgentObservedModel = NULL, AgentRequestedEffort = NULL, AgentObservedEffort = NULL, AgentAdapterContractVersion = NULL WHERE Id = {attempt.Id}");
        Assert.Equal(AgentAttemptInputsStatus.Empty, (await ReadAsync(runId, attempt.Id)).Status);
    }

    // An ordinary string parameter can truncate at an embedded NUL, so the text is stored from bytes cast to TEXT.
    private Task StoreTextAsync(Guid id, string column, byte[] bytes) => column switch
    {
        "AgentRequestedModel" => ExecuteAsync($"UPDATE attempts SET AgentRequestedModel = CAST({bytes} AS TEXT) WHERE Id = {id}"),
        "AgentObservedModel" => ExecuteAsync($"UPDATE attempts SET AgentObservedModel = CAST({bytes} AS TEXT) WHERE Id = {id}"),
        "AgentRequestedEffort" => ExecuteAsync($"UPDATE attempts SET AgentRequestedEffort = CAST({bytes} AS TEXT) WHERE Id = {id}"),
        "AgentObservedEffort" => ExecuteAsync($"UPDATE attempts SET AgentObservedEffort = CAST({bytes} AS TEXT) WHERE Id = {id}"),
        "AgentAdapterContractVersion" => ExecuteAsync($"UPDATE attempts SET AgentAdapterContractVersion = CAST({bytes} AS TEXT) WHERE Id = {id}"),
        _ => throw new ArgumentOutOfRangeException(nameof(column)),
    };

    public static TheoryData<string, object?, bool> MaxTurnStorage() => new()
    {
        { "absent", null, true },
        { "integer 1", 1L, true },
        { "integer 100", 100L, true },
        { "text 7 becomes integer", "7", true },
        { "integer 0", 0L, false },
        { "integer 101", 101L, false },
        { "real 7.5", 7.5d, false },
        { "text abc", "abc", false },
        { "blob 7", new byte[] { 0x37 }, false },
        { "oversized blob", new byte[2_000_000], false },
    };

    [Theory]
    [MemberData(nameof(MaxTurnStorage))]
    public async Task The_exact_max_turn_storage_semantics_are_preserved(string _, object? stored, bool coherent)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 3, maxTurns: 5);
        var messages = await SeedInputsAsync(runId, attempt.Id, 1);
        Assert.Equal(AgentAttemptInputsStatus.Recorded, (await ReadAsync(runId, attempt.Id)).Status);
        await ClaudeMutationTurnLimitTestSupport.SetRawAttemptLimitAsync(_fixture, attempt.Id, stored);

        var result = await ReadAsync(runId, attempt.Id);

        if (coherent)
        {
            Assert.Equal(AgentAttemptInputsStatus.Recorded, result.Status);
            AssertReferences(messages, result.InputMessages);
        }
        else
        {
            AssertUnavailable(result, runId, attempt.Id);
        }
    }

    [Fact]
    public async Task Malformed_identity_or_timestamp_storage_keeps_the_fault_pipeline()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 2);
        var messages = await SeedInputsAsync(runId, attempt.Id, 2);
        await ExecuteAsync($"UPDATE collaboration_messages SET OccurredAtUtc = 'not-a-time' WHERE Id = {messages[1].Id}");

        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(runId, attempt.Id));

        var other = await SeedRunAsync();
        var second = await SeedAttemptAsync(other, 2);
        await SeedInputsAsync(other, second.Id, 2);
        await ExecuteAsync($"UPDATE attempt_input_messages SET CollaborationMessageId = 'not-a-guid' WHERE AttemptId = {second.Id} AND Sequence = 1");

        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(other, second.Id));
    }

    [Theory]
    [InlineData("attempts")]
    [InlineData("attempt_input_messages")]
    [InlineData("collaboration_messages")]
    public async Task A_database_fault_propagates_and_is_not_masked_as_unavailable(string table)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 2);
        await SeedInputsAsync(runId, attempt.Id, 2);

        var fault = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(runId, attempt.Id, new FaultingInterceptor(table)));
        Assert.Equal("injected database fault", fault.Message);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 2);
        await SeedInputsAsync(runId, attempt.Id, 2);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await using var dbContext = _fixture.CreateContext();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GetAgentAttemptInputsQueryHandler(dbContext).HandleAsync(
            new GetAgentAttemptInputsQuery(runId, attempt.Id), cancellation.Token));
    }

    [Fact]
    public async Task The_read_is_three_bounded_scalar_selects_through_the_attempt_sequence_index_that_never_touch_content()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 3, maxTurns: 5);
        await SeedInputsAsync(runId, attempt.Id, 12);
        var recorder = new RecordingInterceptor();

        var result = await SendAsync(runId, attempt.Id, recorder);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentAttemptInputsStatus.Recorded, result.Value.Status);
        Assert.Equal(3, recorder.Commands.Count);
        Assert.All(recorder.Commands, command =>
        {
            Assert.StartsWith("SELECT", command.Text.TrimStart(), StringComparison.OrdinalIgnoreCase);
            foreach (var forbidden in new[] { "OFFSET", "COUNT(", "JOIN", "UPDATE", "INSERT", "DELETE", "\"Summary\"", "\"StructuredContentJson\"" })
            {
                Assert.DoesNotContain(forbidden, command.Text, StringComparison.OrdinalIgnoreCase);
            }
        });

        var attemptRead = Assert.Single(recorder.Commands, command => command.Text.Contains("FROM \"attempts\"", StringComparison.Ordinal)).Text;
        Assert.Contains("\"RunId\" =", attemptRead, StringComparison.Ordinal);
        Assert.Contains("\"Id\" =", attemptRead, StringComparison.Ordinal);
        foreach (var hidden in new[]
        {
            "AgentProviderSessionId", "ProcessArguments", "ProcessExecutablePath", "ProcessWorkingDirectory", "AgentContextManifestArtifactId",
            "AgentGitWorkspaceId", "AgentGitCheckpointId", "AgentResultGitCheckpointId", "AgentCheckpointFingerprintSha256", "AgentOutcome",
            "AgentInputTokens", "AgentOutputTokens", "AgentModelContextLimitsSnapshot", "AgentDirectHumanGuidance", "AgentAccountUsageDecisionSnapshot",
            "AgentRepairSourceAttemptId", "AgentProcessDuration", "AgentTimeout",
        })
        {
            Assert.DoesNotContain(hidden, attemptRead, StringComparison.Ordinal);
        }

        foreach (var column in new[] { "AgentRequestedModel", "AgentObservedModel", "AgentRequestedEffort", "AgentObservedEffort", "AgentAdapterContractVersion" })
        {
            Assert.Matches($@"(?i)length\(CAST\([^)]*""{column}""\s+AS\s+BLOB\)\)\s*(<=|>)\s*384(?!\d)", attemptRead);
        }

        Assert.Matches(@"(?i)length\(CAST\([^)]*""AgentRequestedMaxTurns""\s+AS\s+BLOB\)\)\s*(<=|>)\s*64(?!\d)", attemptRead);

        var inputRead = Assert.Single(recorder.Commands, command => command.Text.Contains("FROM \"attempt_input_messages\"", StringComparison.Ordinal));
        Assert.Contains("\"AttemptId\" =", inputRead.Text, StringComparison.Ordinal);
        Assert.Matches(@"ORDER BY ""[a-z]+""\.""Sequence""", inputRead.Text);
        Assert.Matches(@"(?i)LIMIT\s+(@\w+|65)", inputRead.Text);
        Assert.Matches(@"""Sequence"" % 64\)? = ""[a-z]+""\.""Sequence""", inputRead.Text);
        var limit = inputRead.Parameters.Where(parameter => inputRead.Text.Contains("LIMIT " + parameter.Name, StringComparison.Ordinal)).ToArray();
        Assert.True(inputRead.Text.Contains("LIMIT 65", StringComparison.Ordinal) || Equal65(limit));
        var plan = await ExplainAsync(inputRead);
        Assert.Contains(plan, detail => detail.Contains("IX_attempt_input_messages_AttemptId_Sequence", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, detail => detail.Contains("TEMP B-TREE", StringComparison.Ordinal));

        var messageRead = Assert.Single(recorder.Commands, command => command.Text.Contains("FROM \"collaboration_messages\"", StringComparison.Ordinal)).Text;
        Assert.Contains("\"RunId\" =", messageRead, StringComparison.Ordinal);
        Assert.DoesNotContain("InReplyToMessageId", messageRead, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Provenance\"", messageRead, StringComparison.Ordinal);
    }

    private static bool Equal65(IEnumerable<(string Name, object? Value, SqliteType Type)> parameters) =>
        parameters.Any(parameter => Convert.ToInt64(parameter.Value, CultureInfo.InvariantCulture) == 65);

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
    public async Task An_empty_planner_set_reads_the_attempt_and_its_input_rows_only()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 0);
        var recorder = new RecordingInterceptor();

        var result = await SendAsync(runId, attempt.Id, recorder);

        Assert.Equal(AgentAttemptInputsStatus.Empty, result.Value.Status);
        Assert.Equal(2, recorder.Commands.Count);
        Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("collaboration_messages", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_the_selected_attempt_is_read_never_another_attempt_of_the_run()
    {
        var runId = await SeedRunAsync();
        // The store allows one Running attempt per run, so the earlier attempt is concluded before the next is claimed.
        var older = await SeedAttemptAsync(runId, 0, number: 1);
        await ExecuteAsync($"UPDATE attempts SET Status = 'Completed' WHERE Id = {older.Id}");
        var newer = await SeedAttemptAsync(runId, 2, number: 2);
        var newerMessages = await SeedInputsAsync(runId, newer.Id, 3);

        var olderResult = await ReadAsync(runId, older.Id);
        var newerResult = await ReadAsync(runId, newer.Id);

        Assert.Equal(AgentAttemptInputsStatus.Empty, olderResult.Status);
        AssertHeader(older, olderResult.Attempt);
        Assert.Equal(AgentAttemptInputsStatus.Recorded, newerResult.Status);
        AssertHeader(newer, newerResult.Attempt);
        AssertReferences(newerMessages, newerResult.InputMessages);
    }

    [Fact]
    public async Task Reading_changes_no_stored_row()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, 3, maxTurns: 9);
        await SeedInputsAsync(runId, attempt.Id, 11);

        async Task<string> SnapshotAsync()
        {
            await using var dbContext = _fixture.CreateContext();
            var attempts = await dbContext.Attempts.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
            var messages = await dbContext.CollaborationMessages.AsNoTracking().OrderBy(row => row.Sequence).ToListAsync();
            var inputs = await dbContext.AttemptInputMessages.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
            var events = await dbContext.Events.AsNoTracking().CountAsync();
            var artifacts = await dbContext.Artifacts.AsNoTracking().CountAsync();
            return JsonSerializer.Serialize(attempts.Select(row => new { row.Id, row.Status, row.AttemptNumber, row.AgentRequestedMaxTurns, row.CompletedAtUtc }))
                + JsonSerializer.Serialize(messages.Select(row => new { row.Id, row.Sequence, row.AttemptId, row.Summary, row.OccurredAtUtc }))
                + JsonSerializer.Serialize(inputs.Select(row => new { row.Id, row.AttemptId, row.CollaborationMessageId, row.Sequence }))
                + events + "/" + artifacts;
        }

        var before = await SnapshotAsync();
        var recorder = new RecordingInterceptor();
        await SendAsync(runId, attempt.Id, recorder);
        await SendAsync(runId, Guid.NewGuid(), recorder);

        Assert.Equal(before, await SnapshotAsync());
        Assert.All(recorder.Commands, command => Assert.StartsWith("SELECT", command.Text.TrimStart(), StringComparison.OrdinalIgnoreCase));
    }
}
