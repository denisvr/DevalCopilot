using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputs;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.AgentAttemptOutputsTestStore;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0054 at the query boundary, over real file-backed SQLite with the production migrations: the references of every message that
/// records exactly one Agent attempt as its own attempt are returned only after the whole bounded association set (at most 64 of 65 read
/// rows) was proved, nothing malformed or foreign is filtered away, an empty set is valid for any admitted role, no text is ever
/// selected and nothing is written.
/// </summary>
public sealed class GetAgentAttemptOutputsQueryHandlerTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();
    private readonly AgentAttemptOutputsTestStore _store;

    public GetAgentAttemptOutputsQueryHandlerTests() => _store = new AgentAttemptOutputsTestStore(_fixture);

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Result<GetAgentAttemptOutputsQueryResult>> SendAsync(Guid runId, Guid attemptId, params IInterceptor[] interceptors)
    {
        await using var dbContext = _fixture.CreateContext(interceptors);
        return await new GetAgentAttemptOutputsQueryHandler(dbContext).HandleAsync(
            new GetAgentAttemptOutputsQuery(runId, attemptId), CancellationToken.None);
    }

    private async Task<GetAgentAttemptOutputsQueryResult> ReadAsync(Guid runId, Guid attemptId, params IInterceptor[] interceptors)
    {
        var result = await SendAsync(runId, attemptId, interceptors);
        Assert.True(result.IsSuccess, string.Join(", ", result.IsSuccess ? [] : result.Errors.Select(error => error.Code)));
        return result.Value;
    }

    private static void AssertNotFound(Result<GetAgentAttemptOutputsQueryResult> result, params Guid[] hidden)
    {
        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempt_outputs.not_found", error.Code);
        Assert.Equal(GetAgentAttemptOutputsErrors.NotFoundCode, error.Code);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("The requested Agent attempt was not found.", error.Description);
        foreach (var value in hidden)
        {
            Assert.DoesNotContain(value.ToString(), error.Description, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertUnavailable(GetAgentAttemptOutputsQueryResult result, Guid runId, Guid attemptId)
    {
        Assert.Equal(runId, result.RunId);
        Assert.Equal(attemptId, result.AttemptId);
        Assert.Equal(AgentAttemptOutputsStatus.Unavailable, result.Status);
        Assert.Null(result.Attempt);
        Assert.Null(result.OutputMessageTotalCount);
        Assert.Empty(result.OutputMessages);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    private static void AssertHeader(GetAgentAttemptOutputsQueryResult result, Attempt attempt)
    {
        Assert.NotNull(result.Attempt);
        Assert.Equal(attempt.Id, result.Attempt.Id);
        Assert.Equal(attempt.AttemptNumber, result.Attempt.AttemptNumber);
        Assert.Equal(attempt.AgentRole, result.Attempt.Role);
        Assert.Equal(attempt.AgentProvider, result.Attempt.Provider);
        Assert.Equal(attempt.AgentResponseContract, result.Attempt.ResponseContract);
    }

    private static void AssertRecorded(GetAgentAttemptOutputsQueryResult result, Guid runId, Attempt attempt, IReadOnlyList<CollaborationMessage> messages)
    {
        Assert.Equal(messages.Count == 0 ? AgentAttemptOutputsStatus.Empty : AgentAttemptOutputsStatus.Recorded, result.Status);
        Assert.Equal(runId, result.RunId);
        Assert.Equal(attempt.Id, result.AttemptId);
        AssertHeader(result, attempt);
        Assert.Equal(messages.Count, result.OutputMessageTotalCount);
        Assert.Equal(messages.Count, result.OutputMessages.Count);
        for (var position = 0; position < messages.Count; position++)
        {
            var reference = result.OutputMessages[position];
            Assert.Equal(position, reference.Position);
            Assert.Equal(messages[position].Id, reference.CollaborationMessageId);
            Assert.Equal(messages[position].Type, reference.Type);
            Assert.Equal(messages[position].Sequence, reference.CollaborationSequence);
            Assert.True(messages[position].OccurredAtUtc.EqualsExact(reference.OccurredAtUtc));
        }
    }

    [Fact]
    public async Task An_unknown_run_a_missing_foreign_or_non_agent_attempt_are_one_fixed_not_found()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        await _store.SeedOutputsAsync(attempt, 2);
        var otherRun = await _store.SeedRunAsync();
        var foreign = await _store.SeedAttemptAsync(otherRun);
        await _store.SeedOutputsAsync(foreign, 2);
        var simulatedRun = await _store.SeedRunAsync();
        var simulated = Attempt.Claim(Guid.NewGuid(), simulatedRun, 1, Now);
        await using (var dbContext = _fixture.CreateContext())
        {
            dbContext.Attempts.Add(simulated);
            await dbContext.SaveChangesAsync();
        }

        var processRun = await _store.SeedRunAsync();
        var process = await _store.SeedAttemptAsync(processRun);
        await _store.ExecuteAsync($"UPDATE attempts SET Kind = 'Process' WHERE Id = {process.Id}");
        var unknownId = Guid.NewGuid();

        AssertNotFound(await SendAsync(Guid.NewGuid(), attempt.Id), attempt.Id);
        AssertNotFound(await SendAsync(runId, unknownId), unknownId);
        AssertNotFound(await SendAsync(runId, foreign.Id), foreign.Id);
        AssertNotFound(await SendAsync(runId, Guid.Empty));
        AssertNotFound(await SendAsync(Guid.Empty, attempt.Id));
        AssertNotFound(await SendAsync(simulatedRun, simulated.Id), simulated.Id);
        AssertNotFound(await SendAsync(processRun, process.Id), process.Id);

        Assert.Equal(AgentAttemptOutputsStatus.Recorded, (await ReadAsync(runId, attempt.Id)).Status);
    }

    [Theory]
    [InlineData(AgentRole.Planner, true)]
    [InlineData(AgentRole.CriticalReviewer, true)]
    [InlineData(AgentRole.Resolver, true)]
    [InlineData(AgentRole.Implementer, true)]
    [InlineData(AgentRole.CodeReviewer, true)]
    [InlineData(AgentRole.Planner, false)]
    [InlineData(AgentRole.Resolver, false)]
    [InlineData(AgentRole.Implementer, false)]
    public async Task Zero_associated_messages_is_a_valid_empty_set_for_every_admitted_role_dispatched_or_not(AgentRole role, bool dispatched)
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId, role, dispatched: dispatched);
        // A loose message of the run and another attempt's messages are no associations of this attempt and are never reconstructed.
        await _store.SeedLooseMessageAsync(runId);
        var sibling = await _store.SeedAttemptAsync(runId, AgentRole.Planner, number: 2);
        await _store.SeedOutputsAsync(sibling, 2);
        var recorder = new RecordingInterceptor();

        var result = await ReadAsync(runId, attempt.Id, recorder);

        AssertRecorded(result, runId, attempt, []);
        Assert.Equal(0, result.OutputMessageTotalCount);
        Assert.Equal(2, recorder.Commands.Count);
    }

    [Fact]
    public async Task One_and_several_outputs_are_returned_in_sequence_order_with_their_exact_headers()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId, number: 3);
        var messages = await _store.SeedOutputsAsync(attempt, 5);

        var result = await ReadAsync(runId, attempt.Id);

        AssertRecorded(result, runId, attempt, messages);
        Assert.Equal(
            [CollaborationMessageType.Proposal, CollaborationMessageType.Question, CollaborationMessageType.Decision,
                CollaborationMessageType.Escalation, CollaborationMessageType.Proposal],
            result.OutputMessages.Select(reference => reference.Type));
        var one = await _store.SeedAttemptAsync(runId, AgentRole.Planner, number: 4);
        var single = await _store.SeedOutputsAsync(one, 1);
        AssertRecorded(await ReadAsync(runId, one.Id), runId, one, single);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(64)]
    public async Task Eleven_to_sixty_four_outputs_are_returned_completely_without_a_limit_or_omission(int count)
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, count);

        var result = await ReadAsync(runId, attempt.Id);

        AssertRecorded(result, runId, attempt, messages);
        Assert.Equal(count, result.OutputMessages.Count);
        Assert.Equal(Enumerable.Range(0, count), result.OutputMessages.Select(reference => reference.Position));
    }

    [Fact]
    public async Task A_sixty_fifth_output_makes_the_set_unavailable_with_no_prefix_count_or_text()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        await _store.SeedOutputsAsync(attempt, 65);
        var recorder = new RecordingInterceptor();

        AssertUnavailable(await ReadAsync(runId, attempt.Id, recorder), runId, attempt.Id);

        Assert.Equal(2, recorder.Commands.Count);
        Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("Summary", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(SetDefectData))]
    public async Task Corruption_of_the_last_of_sixty_four_outputs_makes_the_whole_set_unavailable_before_any_reference(string defect)
    {
        var runId = await _store.SeedRunAsync();
        var otherRun = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 64);
        Assert.Equal(AgentAttemptOutputsStatus.Recorded, (await ReadAsync(runId, attempt.Id)).Status);

        await _store.DamageAsync(defect, messages[63], otherRun, attempt);

        var recorder = new RecordingInterceptor();
        AssertUnavailable(await ReadAsync(runId, attempt.Id, recorder), runId, attempt.Id);
        Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("Summary", StringComparison.Ordinal));
        Assert.DoesNotContain(recorder.Commands, command => command.Text.Contains("StructuredContentJson", StringComparison.Ordinal));
    }

    public static TheoryData<string> SetDefectData() => [.. SetDefects];

    [Fact]
    public async Task A_message_of_this_attempt_recorded_in_another_run_is_refused_not_hidden()
    {
        var runId = await _store.SeedRunAsync();
        var otherRun = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 3);
        await _store.ExecuteAsync($"UPDATE collaboration_messages SET RunId = {otherRun} WHERE Id = {messages[0].Id}");

        // A first member that belongs elsewhere is not skipped to show the others.
        AssertUnavailable(await ReadAsync(runId, attempt.Id), runId, attempt.Id);
    }

    [Fact]
    public async Task An_attempt_whose_identity_cannot_be_proved_is_unavailable_and_reads_no_message()
    {
        foreach (var (column, stored) in new[] { ("AgentRole", "Overlord"), ("AgentProvider", "Gemini"), ("AgentResponseContract", "Nonsense"), ("AgentRole", null) })
        {
            var runId = await _store.SeedRunAsync();
            var attempt = await _store.SeedAttemptAsync(runId);
            await _store.SeedOutputsAsync(attempt, 2);
            var value = stored is null ? null : stored + Secret;
            var sql = $"UPDATE attempts SET {column} = @value WHERE Id = @id";
            await using (var dbContext = _fixture.CreateContext())
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    sql,
                    new SqliteParameter("@value", value ?? (object)DBNull.Value),
                    new SqliteParameter("@id", attempt.Id.ToString().ToUpperInvariant()));
            }

            var recorder = new RecordingInterceptor();
            AssertUnavailable(await ReadAsync(runId, attempt.Id, recorder), runId, attempt.Id);
            Assert.Single(recorder.Commands);
            Assert.Contains("FROM \"attempts\"", recorder.Commands[0].Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_nonempty_set_of_an_undispatched_attempt_is_unavailable_but_its_empty_set_is_not()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        await _store.SeedOutputsAsync(attempt, 2);
        await _store.ExecuteAsync($"UPDATE attempts SET AgentDispatchedAtUtc = NULL WHERE Id = {attempt.Id}");

        AssertUnavailable(await ReadAsync(runId, attempt.Id), runId, attempt.Id);
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

            var result = await ReadAsync(runId, attempt.Id);

            Assert.Equal(AgentAttemptOutputsStatus.Recorded, result.Status);
            Assert.Equal(sequence, result.OutputMessages[1].CollaborationSequence);
            Assert.True(messages[1].OccurredAtUtc.EqualsExact(result.OutputMessages[1].OccurredAtUtc));
            Assert.Equal(1, result.OutputMessages[1].Position);
        }
    }

    [Fact]
    public async Task Malformed_identity_or_timestamp_storage_keeps_the_fault_pipeline()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        var messages = await _store.SeedOutputsAsync(attempt, 2);
        await _store.ExecuteAsync($"UPDATE collaboration_messages SET OccurredAtUtc = 'not-a-time' WHERE Id = {messages[1].Id}");

        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(runId, attempt.Id));

        var other = await _store.SeedRunAsync();
        var second = await _store.SeedAttemptAsync(other);
        var secondMessages = await _store.SeedOutputsAsync(second, 2);
        await _store.ExecuteAsync($"UPDATE collaboration_messages SET Id = 'not-a-guid' WHERE Id = {secondMessages[1].Id}");

        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(other, second.Id));
    }

    [Theory]
    [InlineData("attempts")]
    [InlineData("collaboration_messages")]
    public async Task A_database_fault_propagates_and_is_not_masked_as_unavailable(string table)
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        await _store.SeedOutputsAsync(attempt, 2);

        var fault = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(
            runId, attempt.Id, new FaultingInterceptor(text => text.Contains($"FROM \"{table}\"", StringComparison.Ordinal))));

        Assert.Equal("injected database fault", fault.Message);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        await _store.SeedOutputsAsync(attempt, 2);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await using var dbContext = _fixture.CreateContext();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GetAgentAttemptOutputsQueryHandler(dbContext).HandleAsync(
            new GetAgentAttemptOutputsQuery(runId, attempt.Id), cancellation.Token));
    }

    [Fact]
    public async Task The_read_is_two_bounded_indexed_scalar_selects_that_never_touch_text_or_follow_a_relationship()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        await _store.SeedOutputsAsync(attempt, 12);
        var recorder = new RecordingInterceptor();

        var result = await ReadAsync(runId, attempt.Id, recorder);

        Assert.Equal(AgentAttemptOutputsStatus.Recorded, result.Status);
        Assert.Equal(2, recorder.Commands.Count);
        Assert.All(recorder.Commands, command =>
        {
            Assert.StartsWith("SELECT", command.Text.TrimStart(), StringComparison.OrdinalIgnoreCase);
            foreach (var forbidden in new[]
            {
                "Summary", "StructuredContentJson", "InReplyToMessageId", "OFFSET", "COUNT(", "JOIN", "UPDATE", "INSERT", "DELETE",
                "\"events\"", "\"artifacts\"", "attempt_input_messages", "RecipientKind",
            })
            {
                Assert.DoesNotContain(forbidden, command.Text, StringComparison.OrdinalIgnoreCase);
            }
        });

        var headers = recorder.Commands[1];
        Assert.Contains("FROM \"collaboration_messages\"", headers.Text, StringComparison.Ordinal);
        Assert.Contains("\"AttemptId\" =", headers.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"RunId\" =", headers.Text, StringComparison.Ordinal);
        Assert.Matches(@"(?i)ORDER BY [^\n]*""Sequence""", headers.Text);
        Assert.Matches(@"(?i)LIMIT @", headers.Text);
        Assert.Contains(headers.Parameters, parameter => parameter.Value is int or long && Convert.ToInt64(parameter.Value) == 65);

        var plan = await _store.ExplainAsync(headers);
        Assert.Contains(plan, detail => detail.Contains("IX_collaboration_messages_AttemptId", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, detail => detail.Contains("TEMP B-TREE", StringComparison.Ordinal));
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

        AssertRecorded(await ReadAsync(olderRun, older.Id), olderRun, older, olderMessages);
        AssertRecorded(await ReadAsync(olderRun, newerSameRun.Id), olderRun, newerSameRun, newerSameRunMessages);
        AssertRecorded(await ReadAsync(newerRun, newer.Id), newerRun, newer, newerMessages);
        AssertNotFound(await SendAsync(newerRun, older.Id));
    }

    [Fact]
    public async Task Reading_changes_no_stored_row_and_writes_nothing()
    {
        var runId = await _store.SeedRunAsync();
        var attempt = await _store.SeedAttemptAsync(runId);
        await _store.SeedOutputsAsync(attempt, 11);
        var before = await _store.SnapshotAsync();
        var recorder = new RecordingInterceptor();

        await SendAsync(runId, attempt.Id, recorder);
        await SendAsync(runId, Guid.NewGuid(), recorder);
        await SendAsync(Guid.NewGuid(), attempt.Id, recorder);

        Assert.Equal(before, await _store.SnapshotAsync());
        Assert.All(recorder.Commands, command => Assert.StartsWith("SELECT", command.Text.TrimStart(), StringComparison.OrdinalIgnoreCase));
    }
}
