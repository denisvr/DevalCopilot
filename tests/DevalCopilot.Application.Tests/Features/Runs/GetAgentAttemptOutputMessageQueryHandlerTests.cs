using System.Text;
using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputMessage;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputs;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.AgentAttemptOutputsTestStore;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0054 at the query boundary, over real file-backed SQLite with the production migrations: every read proves the whole bounded
/// output set of exactly one Agent attempt again, exactly as the reference inspection does, before it selects the summary and content of
/// exactly the requested member inside the same run and attempt association through the unchanged ADR-0035/0036 admission. A nonmember is
/// the fixed not-found, an unprovable set or a reread association, header, provenance or actor that disagrees with its admitted
/// reference is unavailable without any part of the set, unavailable content keeps the reference, and nothing is written or substituted.
/// </summary>
public sealed class GetAgentAttemptOutputMessageQueryHandlerTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();
    private readonly AgentAttemptOutputsTestStore _store;

    public GetAgentAttemptOutputMessageQueryHandlerTests() => _store = new AgentAttemptOutputsTestStore(_fixture);

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Result<GetAgentAttemptOutputMessageQueryResult>> SendAsync(
        Guid runId, Guid attemptId, Guid messageId, params IInterceptor[] interceptors)
    {
        await using var dbContext = _fixture.CreateContext(interceptors);
        return await new GetAgentAttemptOutputMessageQueryHandler(dbContext).HandleAsync(
            new GetAgentAttemptOutputMessageQuery(runId, attemptId, messageId), CancellationToken.None);
    }

    private async Task<GetAgentAttemptOutputMessageQueryResult> ReadAsync(
        Guid runId, Guid attemptId, Guid messageId, params IInterceptor[] interceptors)
    {
        var result = await SendAsync(runId, attemptId, messageId, interceptors);
        Assert.True(result.IsSuccess, string.Join(", ", result.IsSuccess ? [] : result.Errors.Select(error => error.Code)));
        return result.Value;
    }

    private static void AssertNotFound(Result<GetAgentAttemptOutputMessageQueryResult> result, params Guid[] hidden)
    {
        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempt_output_message.not_found", error.Code);
        Assert.Equal(GetAgentAttemptOutputMessageErrors.NotFoundCode, error.Code);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("The requested Agent attempt output message was not found.", error.Description);
        foreach (var value in hidden)
        {
            Assert.DoesNotContain(value.ToString(), error.Description, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertUnavailable(GetAgentAttemptOutputMessageQueryResult result, Guid runId, Guid attemptId, Guid messageId)
    {
        Assert.Equal(runId, result.RunId);
        Assert.Equal(attemptId, result.AttemptId);
        Assert.Equal(messageId, result.CollaborationMessageId);
        Assert.Equal(AgentAttemptOutputMessageStatus.Unavailable, result.Status);
        Assert.Null(result.Attempt);
        Assert.Null(result.Output);
        Assert.Null(result.Message);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(Secret, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Recorded summary", serialized, StringComparison.Ordinal);
    }

    /// <summary>The header, the selected reference and the message header equal the attempt and the stored message; content is not judged.</summary>
    private static void AssertRecordedHeader(
        GetAgentAttemptOutputMessageQueryResult result, Guid runId, Attempt attempt, CollaborationMessage message, int position)
    {
        Assert.Equal(AgentAttemptOutputMessageStatus.Recorded, result.Status);
        Assert.Equal(runId, result.RunId);
        Assert.Equal(attempt.Id, result.AttemptId);
        Assert.Equal(message.Id, result.CollaborationMessageId);
        Assert.NotNull(result.Attempt);
        Assert.Equal(attempt.Id, result.Attempt.Id);
        Assert.Equal(attempt.AttemptNumber, result.Attempt.AttemptNumber);
        Assert.Equal(attempt.AgentRole, result.Attempt.Role);
        Assert.Equal(attempt.AgentProvider, result.Attempt.Provider);
        Assert.Equal(attempt.AgentResponseContract, result.Attempt.ResponseContract);
        Assert.NotNull(result.Output);
        Assert.Equal(position, result.Output.Position);
        Assert.Equal(message.Id, result.Output.CollaborationMessageId);
        Assert.Equal(message.Type, result.Output.Type);
        Assert.Equal(message.Sequence, result.Output.CollaborationSequence);
        Assert.True(message.OccurredAtUtc.EqualsExact(result.Output.OccurredAtUtc));
        Assert.NotNull(result.Message);
        Assert.Equal(runId, result.Message.RunId);
        Assert.Equal(message.Id, result.Message.Id);
        Assert.Equal(message.Sequence, result.Message.Sequence);
        Assert.True(message.OccurredAtUtc.EqualsExact(result.Message.OccurredAtUtc));
        Assert.Equal("1.0", result.Message.ProtocolVersion);
        Assert.Equal(message.Type.ToString(), result.Message.Type);
        Assert.Equal("ProviderObserved", result.Message.Provenance);
        Assert.Equal(
            new CollaborationHistoryParticipant(ParticipantKind.Agent, attempt.AgentRole, attempt.AgentProvider), result.Message.Actor);
    }

    private static void AssertContentUnavailable(GetAgentAttemptOutputMessageQueryResult result)
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
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var members = await _store.SeedOutputsAsync(attempt, 3);
        var loose = await _store.SeedLooseMessageAsync(runId);
        var sibling = await _store.SeedAttemptAsync(runId, AgentRole.Planner, number: 2);
        var siblingMembers = await _store.SeedOutputsAsync(sibling, 2);
        var otherRun = await _store.SeedRunAsync();
        var foreign = await _store.SeedAttemptAsync(otherRun);
        var foreignMembers = await _store.SeedOutputsAsync(foreign, 2);
        var simulatedRun = await _store.SeedRunAsync();
        var simulated = Attempt.Claim(Guid.NewGuid(), simulatedRun, 1, Now);
        await using (var dbContext = _fixture.CreateContext())
        {
            dbContext.Attempts.Add(simulated);
            await dbContext.SaveChangesAsync();
        }

        var processRun = await _store.SeedRunAsync();
        var process = await _store.SeedAttemptAsync(processRun);
        var processMembers = await _store.SeedOutputsAsync(process, 1);
        await _store.ExecuteAsync($"UPDATE attempts SET Kind = 'Process' WHERE Id = {process.Id}");
        var emptyRun = await _store.SeedRunAsync();
        var empty = await _store.SeedAttemptAsync(emptyRun);
        var emptyRunMessage = await _store.SeedLooseMessageAsync(emptyRun);
        var unknownId = Guid.NewGuid();

        AssertNotFound(await SendAsync(Guid.NewGuid(), attempt.Id, members[0].Id), attempt.Id, members[0].Id);
        AssertNotFound(await SendAsync(runId, unknownId, members[0].Id), unknownId);
        AssertNotFound(await SendAsync(runId, foreign.Id, foreignMembers[0].Id), foreign.Id);
        AssertNotFound(await SendAsync(runId, Guid.Empty, members[0].Id));
        AssertNotFound(await SendAsync(simulatedRun, simulated.Id, members[0].Id), simulated.Id);
        AssertNotFound(await SendAsync(processRun, process.Id, processMembers[0].Id), process.Id);
        // Nonmembers of a fully proved set: a message of the same run that records no attempt, a sibling attempt's output, another run's
        // output, an unknown identity, the empty identity, and any message for a coherent empty set.
        AssertNotFound(await SendAsync(runId, attempt.Id, loose.Id), loose.Id);
        AssertNotFound(await SendAsync(runId, attempt.Id, siblingMembers[0].Id), siblingMembers[0].Id);
        AssertNotFound(await SendAsync(runId, attempt.Id, foreignMembers[0].Id), foreignMembers[0].Id);
        AssertNotFound(await SendAsync(runId, attempt.Id, unknownId), unknownId);
        AssertNotFound(await SendAsync(runId, attempt.Id, Guid.Empty));
        AssertNotFound(await SendAsync(emptyRun, empty.Id, emptyRunMessage.Id), emptyRunMessage.Id);

        Assert.Equal(AgentAttemptOutputMessageStatus.Recorded, (await ReadAsync(runId, attempt.Id, members[2].Id)).Status);
    }

    [Fact]
    public async Task A_nonmember_reads_no_summary_or_content_of_any_message()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        await _store.SeedOutputsAsync(attempt, 3);
        var loose = await _store.SeedLooseMessageAsync(runId);
        var recorder = new RecordingInterceptor();

        AssertNotFound(await SendAsync(runId, attempt.Id, loose.Id, recorder));

        Assert.Equal(2, recorder.Commands.Count);
        Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("Summary", StringComparison.Ordinal));
        Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("StructuredContentJson", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AgentRole.Planner)]
    [InlineData(AgentRole.CriticalReviewer)]
    [InlineData(AgentRole.Resolver)]
    [InlineData(AgentRole.Implementer)]
    [InlineData(AgentRole.CodeReviewer)]
    public async Task Every_message_type_a_role_may_author_is_returned_with_its_admitted_summary_and_fields_in_stored_order(AgentRole role)
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId, role, number: 4);
        var count = TypesOf(role).Length * 2;
        var messages = await _store.SeedOutputsAsync(attempt, count);

        for (var position = 0; position < messages.Count; position++)
        {
            var message = messages[position];
            var result = await ReadAsync(runId, attempt.Id, message.Id);

            AssertRecordedHeader(result, runId, attempt, message, position);
            Assert.True(result.Message!.ContentAvailable);
            Assert.Equal($"Recorded summary {position}.", result.Message.Summary);
            Assert.Equal(
                CollaborationHistoryFixtureContent.FieldNames[message.Type].Select(name => (name, CollaborationHistoryFixtureContent.Value(name, position))),
                result.Message.Fields.Select(field => (field.Name, field.Value)));
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
    public async Task Any_member_of_a_set_up_to_sixty_four_is_reachable_including_those_after_the_first_ten(int count, int position)
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, count);

        var result = await ReadAsync(runId, attempt.Id, messages[position].Id);

        AssertRecordedHeader(result, runId, attempt, messages[position], position);
        Assert.True(result.Message!.ContentAvailable);
        Assert.Equal($"Recorded summary {position}.", result.Message.Summary);
    }

    [Fact]
    public async Task A_sixty_fifth_output_makes_every_member_unavailable_and_reads_no_text()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 65);
        var stranger = Guid.NewGuid();

        foreach (var member in new[] { messages[0].Id, messages[63].Id, messages[64].Id, stranger })
        {
            var recorder = new RecordingInterceptor();
            AssertUnavailable(await ReadAsync(runId, attempt.Id, member, recorder), runId, attempt.Id, member);
            Assert.Equal(2, recorder.Commands.Count);
            Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("Summary", StringComparison.Ordinal));
        }
    }

    [Theory]
    [MemberData(nameof(SetDefectData))]
    public async Task Corruption_of_the_last_of_sixty_four_outputs_makes_the_first_unavailable_before_any_text_is_read(string defect)
    {
        var runId = await _store.SeedRunAsync();
        var otherRun = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 64);
        Assert.Equal(AgentAttemptOutputMessageStatus.Recorded, (await ReadAsync(runId, attempt.Id, messages[0].Id)).Status);
        var last = messages[63];

        await _store.DamageAsync(defect, last, otherRun, attempt);

        var recorder = new RecordingInterceptor();
        AssertUnavailable(await ReadAsync(runId, attempt.Id, messages[0].Id, recorder), runId, attempt.Id, messages[0].Id);
        Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("Summary", StringComparison.Ordinal));
        Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("StructuredContentJson", StringComparison.Ordinal));
        // Neither is the corrupt member itself found or shown, nor does a stranger's identity become a not-found: the set is not filtered.
        AssertUnavailable(await ReadAsync(runId, attempt.Id, last.Id), runId, attempt.Id, last.Id);
        var stranger = Guid.NewGuid();
        AssertUnavailable(await ReadAsync(runId, attempt.Id, stranger), runId, attempt.Id, stranger);
    }

    public static TheoryData<string> SetDefectData() => [.. SetDefects];

    [Theory]
    [InlineData("AgentRole", "Overlord")]
    [InlineData("AgentProvider", "Gemini")]
    [InlineData("AgentResponseContract", "Nonsense")]
    [InlineData("AgentRole", null)]
    [InlineData("AttemptNumber", "0")]
    public async Task An_attempt_whose_identity_cannot_be_proved_is_unavailable_and_reads_neither_headers_nor_text(string column, string? stored)
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 2);
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
    public async Task An_attempt_with_no_associated_message_is_a_valid_empty_set_so_every_message_is_not_found()
    {
        var runId = await _store.SeedRunAsync();
        var undispatched = await _store.SeedAttemptAsync(runId, AgentRole.Planner, dispatched: false);
        var loose = await _store.SeedLooseMessageAsync(runId);

        AssertNotFound(await SendAsync(runId, undispatched.Id, loose.Id), loose.Id);
    }

    [Theory]
    [InlineData("RecipientKind", "Wizard")]
    [InlineData("RecipientAgentRole", "Overlord")]
    [InlineData("RecipientAgentProvider", "Gemini")]
    public async Task An_unrecognized_recipient_keeps_the_reference_with_content_unavailable(string column, string stored)
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 3);
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
        Assert.Null(result.Message!.Recipient.Kind);
        Assert.DoesNotContain(stored, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        // A sibling's content is unaffected, and so are its reference and the set.
        Assert.True((await ReadAsync(runId, attempt.Id, messages[2].Id)).Message!.ContentAvailable);
        Assert.Equal(AgentAttemptOutputsStatus.Recorded, await ListStatusAsync(runId, attempt.Id));
    }

    [Fact]
    public async Task An_incoherent_recipient_keeps_the_reference_with_content_unavailable()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 2);
        // The recipient equals the actor, which the content admission refuses without invalidating the association.
        await _store.ExecuteAsync(
            $"UPDATE collaboration_messages SET RecipientKind = 'Agent', RecipientAgentRole = 'Resolver', RecipientAgentProvider = 'Codex' WHERE Id = {messages[0].Id}");

        var result = await ReadAsync(runId, attempt.Id, messages[0].Id);

        AssertRecordedHeader(result, runId, attempt, messages[0], 0);
        AssertContentUnavailable(result);
    }

    private async Task<AgentAttemptOutputsStatus> ListStatusAsync(Guid runId, Guid attemptId)
    {
        await using var dbContext = _fixture.CreateContext();
        var result = await new GetAgentAttemptOutputsQueryHandler(dbContext).HandleAsync(
            new GetAgentAttemptOutputsQuery(runId, attemptId), CancellationToken.None);
        return result.Value.Status;
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
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 3);
        Assert.Equal(CollaborationMessageType.Proposal, messages[0].Type);
        if (column == "summary")
        {
            await _store.ExecuteAsync($"UPDATE collaboration_messages SET Summary = {stored} WHERE Id = {messages[0].Id}");
        }
        else
        {
            await _store.ExecuteAsync($"UPDATE collaboration_messages SET StructuredContentJson = {stored} WHERE Id = {messages[0].Id}");
        }

        var result = await ReadAsync(runId, attempt.Id, messages[0].Id);

        AssertRecordedHeader(result, runId, attempt, messages[0], 0);
        AssertContentUnavailable(result);
        Assert.True(JsonSerializer.Serialize(result).Length < 10_000);
        Assert.Equal(AgentAttemptOutputsStatus.Recorded, await ListStatusAsync(runId, attempt.Id));
    }

    [Theory]
    [InlineData("Summary")]
    [InlineData("StructuredContentJson")]
    public async Task Oversized_text_hidden_behind_an_embedded_nul_is_not_selected_and_content_stays_unavailable(string column)
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 2);
        var valid = column == "Summary" ? "Safe summary" : CollaborationHistoryFixtureContent.Json(CollaborationMessageType.Proposal);
        var bytes = Encoding.UTF8.GetBytes(valid + "\0" + new string('x', 2_000_000) + Secret);
        await using (var dbContext = _fixture.CreateContext())
        {
            // An ordinary string parameter can truncate at an embedded NUL, so the text is stored from UTF-8 bytes cast to TEXT.
            var sql = column == "Summary"
                ? "UPDATE collaboration_messages SET Summary = CAST({0} AS TEXT) WHERE Id = {1}"
                : "UPDATE collaboration_messages SET StructuredContentJson = CAST({0} AS TEXT) WHERE Id = {1}";
            await dbContext.Database.ExecuteSqlRawAsync(sql, bytes, messages[0].Id);
        }

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
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 2);
        Assert.Equal(1234567, messages[1].OccurredAtUtc.Ticks % TimeSpan.TicksPerSecond);

        foreach (var sequence in new[] { 9007199254740993L, 9223372036854775806L })
        {
            await _store.ExecuteWithoutForeignKeysAsync($"UPDATE collaboration_messages SET Sequence = {sequence} WHERE Id = {messages[1].Id}");

            var result = await ReadAsync(runId, attempt.Id, messages[1].Id);

            Assert.Equal(AgentAttemptOutputMessageStatus.Recorded, result.Status);
            Assert.Equal(sequence, result.Output!.CollaborationSequence);
            Assert.Equal(sequence, result.Message!.Sequence);
            Assert.True(messages[1].OccurredAtUtc.EqualsExact(result.Output.OccurredAtUtc));
            Assert.True(messages[1].OccurredAtUtc.EqualsExact(result.Message.OccurredAtUtc));
            Assert.True(result.Message.ContentAvailable);
        }
    }

    [Fact]
    public async Task Malformed_identity_or_timestamp_storage_keeps_the_fault_pipeline()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 2);
        await _store.ExecuteAsync($"UPDATE collaboration_messages SET OccurredAtUtc = 'not-a-time' WHERE Id = {messages[1].Id}");

        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(runId, attempt.Id, messages[0].Id));

        var other = await _store.SeedRunAsync();
        var second = await _store.SeedAttemptAsync(other);
        var secondMessages = await _store.SeedOutputsAsync(second, 2);
        await _store.ExecuteAsync($"UPDATE collaboration_messages SET Id = 'not-a-guid' WHERE Id = {secondMessages[1].Id}");

        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(other, second.Id, secondMessages[0].Id));
    }

    public static TheoryData<string, string> RereadDisagreements() => new()
    {
        { "type", "UPDATE collaboration_messages SET Type = 'Question' WHERE Id = '{0}'" },
        { "unknown-type", "UPDATE collaboration_messages SET Type = 'Overlord" + Secret + "' WHERE Id = '{0}'" },
        { "version", "UPDATE collaboration_messages SET ProtocolVersion = '2.0' WHERE Id = '{0}'" },
        { "sequence", "UPDATE collaboration_messages SET Sequence = Sequence + 1000 WHERE Id = '{0}'" },
        { "time", "UPDATE collaboration_messages SET OccurredAtUtc = '2030-01-01 00:00:00+00:00' WHERE Id = '{0}'" },
        { "run", "UPDATE collaboration_messages SET RunId = '{1}' WHERE Id = '{0}'" },
        { "deleted", "DELETE FROM collaboration_messages WHERE Id = '{0}'" },
        { "association-removed", "UPDATE collaboration_messages SET AttemptId = NULL WHERE Id = '{0}'" },
        { "association-moved", "UPDATE collaboration_messages SET AttemptId = '{2}' WHERE Id = '{0}'" },
        { "actor-role", "UPDATE collaboration_messages SET ActorAgentRole = 'CodeReviewer' WHERE Id = '{0}'" },
        { "actor-role-removed", "UPDATE collaboration_messages SET ActorAgentRole = NULL WHERE Id = '{0}'" },
        { "actor-provider", "UPDATE collaboration_messages SET ActorAgentProvider = 'ClaudeCode' WHERE Id = '{0}'" },
        { "actor-kind", "UPDATE collaboration_messages SET ActorKind = 'Orchestrator', ActorAgentRole = NULL, ActorAgentProvider = NULL WHERE Id = '{0}'" },
        { "provenance", "UPDATE collaboration_messages SET Provenance = 'HostConstructed' WHERE Id = '{0}'" },
        { "unknown-provenance", "UPDATE collaboration_messages SET Provenance = 'Forged" + Secret + "' WHERE Id = '{0}'" },
    };

    [Theory]
    [MemberData(nameof(RereadDisagreements))]
    public async Task A_reread_that_disagrees_with_the_admitted_reference_is_unavailable_never_a_substitute(string _, string template)
    {
        var runId = await _store.SeedRunAsync();
        var otherRun = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var sibling = await _store.SeedAttemptAsync(runId, AgentRole.Planner, number: 2);
        var messages = await _store.SeedOutputsAsync(attempt, 3);
        var changer = new BeforeContentReadInterceptor(
            _fixture.ConnectionString,
            string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                template,
                messages[0].Id.ToString().ToUpperInvariant(),
                otherRun.ToString().ToUpperInvariant(),
                sibling.Id.ToString().ToUpperInvariant()));

        var result = await ReadAsync(runId, attempt.Id, messages[0].Id, changer);

        Assert.Equal(1, changer.Applied);
        AssertUnavailable(result, runId, attempt.Id, messages[0].Id);
    }

    [Fact]
    public async Task Content_changed_between_the_reads_is_shown_as_reread_because_separate_reads_claim_no_snapshot()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 2);
        var changer = new BeforeContentReadInterceptor(
            _fixture.ConnectionString,
            $"UPDATE collaboration_messages SET Summary = 'Reread summary.' WHERE Id = '{messages[1].Id.ToString().ToUpperInvariant()}'");

        var result = await ReadAsync(runId, attempt.Id, messages[1].Id, changer);

        AssertRecordedHeader(result, runId, attempt, messages[1], 1);
        Assert.Equal("Reread summary.", result.Message!.Summary);
    }

    [Theory]
    [InlineData("attempts")]
    [InlineData("headers")]
    [InlineData("content")]
    public async Task A_database_fault_propagates_and_is_not_masked_as_unavailable(string read)
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 2);
        Func<string, bool> matches = read switch
        {
            "attempts" => text => text.Contains("FROM \"attempts\"", StringComparison.Ordinal),
            "headers" => text => text.Contains("FROM \"collaboration_messages\"", StringComparison.Ordinal)
                && !text.Contains("\"Summary\"", StringComparison.Ordinal),
            _ => text => text.Contains("\"Summary\"", StringComparison.Ordinal),
        };

        var fault = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SendAsync(runId, attempt.Id, messages[0].Id, new FaultingInterceptor(matches)));
        Assert.Equal("injected database fault", fault.Message);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 2);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await using var dbContext = _fixture.CreateContext();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GetAgentAttemptOutputMessageQueryHandler(dbContext).HandleAsync(
            new GetAgentAttemptOutputMessageQuery(runId, attempt.Id, messages[0].Id), cancellation.Token));
    }

    [Fact]
    public async Task The_read_is_the_unchanged_set_proof_then_one_guarded_select_of_exactly_that_member()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 12);
        var proof = new RecordingInterceptor();
        var recorder = new RecordingInterceptor();

        await using (var dbContext = _fixture.CreateContext(proof))
        {
            await new GetAgentAttemptOutputsQueryHandler(dbContext).HandleAsync(
                new GetAgentAttemptOutputsQuery(runId, attempt.Id), CancellationToken.None);
        }

        var result = await SendAsync(runId, attempt.Id, messages[11].Id, recorder);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentAttemptOutputMessageStatus.Recorded, result.Value.Status);
        Assert.Equal(3, recorder.Commands.Count);
        // The first two statements are exactly the reference inspection's proof, which never touches text.
        Assert.Equal(proof.Commands.Select(command => command.Text), recorder.Commands.Take(2).Select(command => command.Text));
        Assert.All(recorder.Commands.Take(2), command => Assert.DoesNotContain("Summary", command.Text, StringComparison.Ordinal));
        Assert.All(recorder.Commands, command =>
        {
            Assert.StartsWith("SELECT", command.Text.TrimStart(), StringComparison.OrdinalIgnoreCase);
            foreach (var forbidden in new[] { "OFFSET", "COUNT(", "JOIN", "UPDATE", "INSERT", "DELETE", "InReplyToMessageId", " IN (" })
            {
                Assert.DoesNotContain(forbidden, command.Text, StringComparison.OrdinalIgnoreCase);
            }
        });

        var content = recorder.Commands[2];
        Assert.Contains("FROM \"collaboration_messages\"", content.Text, StringComparison.Ordinal);
        Assert.Contains("\"RunId\" =", content.Text, StringComparison.Ordinal);
        Assert.Contains("\"AttemptId\" =", content.Text, StringComparison.Ordinal);
        Assert.Contains("\"Id\" =", content.Text, StringComparison.Ordinal);
        Assert.Matches(@"(?i)length\(CAST\([^)]*""Summary""\s+AS\s+BLOB\)\)\s*<=\s*1800(?!\d)", content.Text);
        Assert.Matches(@"(?i)length\(CAST\([^)]*""StructuredContentJson""\s+AS\s+BLOB\)\)\s*<=\s*15000(?!\d)", content.Text);
        // Exactly the requested member is a parameter; no other message identity is.
        var identities = content.Parameters.Select(parameter => parameter.Value?.ToString() ?? string.Empty).ToArray();
        Assert.Contains(identities, value => string.Equals(value, messages[11].Id.ToString(), StringComparison.OrdinalIgnoreCase));
        foreach (var other in messages.Take(11))
        {
            Assert.DoesNotContain(identities, value => string.Equals(value, other.Id.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        var plan = await _store.ExplainAsync(content);
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN", StringComparison.Ordinal));
        Assert.Contains(plan, detail => detail.StartsWith("SEARCH", StringComparison.Ordinal));
        Assert.Single(recorder.Commands, command => command.Text.Contains("\"Summary\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Exact_ownership_holds_despite_newer_runs_attempts_and_messages()
    {
        var olderRun = await _store.SeedRunAsync();
        var older = await _store.SeedAttemptAsync(olderRun, number: 1);
        await _store.ExecuteAsync($"UPDATE attempts SET Status = 'Completed' WHERE Id = {older.Id}");
        var olderMessages = await _store.SeedOutputsAsync(older, 4);
        var newerSameRun = await _store.SeedAttemptAsync(olderRun, number: 2);
        var newerSameRunMessages = await _store.SeedOutputsAsync(newerSameRun, 3);
        var newerRun = await _store.SeedRunAsync();
        var newer = await _store.SeedAttemptAsync(newerRun);
        var newerMessages = await _store.SeedOutputsAsync(newer, 2);

        var result = await ReadAsync(olderRun, older.Id, olderMessages[2].Id);

        AssertRecordedHeader(result, olderRun, older, olderMessages[2], 2);
        AssertNotFound(await SendAsync(olderRun, older.Id, newerSameRunMessages[0].Id));
        AssertNotFound(await SendAsync(olderRun, older.Id, newerMessages[0].Id));
        AssertNotFound(await SendAsync(newerRun, older.Id, olderMessages[0].Id));
        AssertRecordedHeader(await ReadAsync(newerRun, newer.Id, newerMessages[1].Id), newerRun, newer, newerMessages[1], 1);
        AssertRecordedHeader(
            await ReadAsync(olderRun, newerSameRun.Id, newerSameRunMessages[2].Id), olderRun, newerSameRun, newerSameRunMessages[2], 2);
    }

    [Fact]
    public async Task Reading_changes_no_stored_row()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 11);
        var before = await _store.SnapshotAsync();
        var recorder = new RecordingInterceptor();

        await SendAsync(runId, attempt.Id, messages[10].Id, recorder);
        await SendAsync(runId, attempt.Id, Guid.NewGuid(), recorder);
        await SendAsync(runId, Guid.NewGuid(), messages[0].Id, recorder);

        Assert.Equal(before, await _store.SnapshotAsync());
        Assert.All(recorder.Commands, command => Assert.StartsWith("SELECT", command.Text.TrimStart(), StringComparison.OrdinalIgnoreCase));
    }
}
