using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0035 at the query boundary, over real file-backed SQLite with the production migrations: the structural bounds are refused
/// rather than clamped, the sequence cursor is exact and exclusive, the page is a fixed read-only indexed select whose text columns
/// are bounded by the database, and unknown, malformed, unsafe or oversized stored values keep their header as unavailable without
/// ever exposing the stored text or affecting a healthy neighbor.
/// </summary>
public sealed class GetCollaborationHistoryQueryHandlerTests : IAsyncLifetime
{
    private const string Secret = "SECRET-SENTINEL-41d9";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class RecordingInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public List<(string Text, (string Name, object? Value)[] Parameters)> Reads { get; } = [];

        private void Record(DbCommand command)
        {
            Commands.Add(command.CommandText);
            Reads.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value)).ToArray()));
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private async Task<Guid> SeedRunAsync()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "History", $@"C:\repos\{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        dbContext.Projects.Add(project);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", DateTimeOffset.UtcNow);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    private static CollaborationMessage Build(Guid runId, CollaborationMessageType type, int index)
    {
        var human = type == CollaborationMessageType.HumanInstruction;
        return CollaborationMessage.Record(
            Guid.NewGuid(),
            runId,
            null,
            CollaborationMessage.ProtocolVersionOne,
            human ? ParticipantIdentity.ForHuman() : ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForOrchestrator(),
            type,
            type == CollaborationMessageType.Proposal ? null : Guid.NewGuid(),
            $"Recorded {type} summary {index}.",
            CollaborationHistoryFixtureContent.Json(type, index),
            human ? CollaborationMessageProvenance.HumanSubmitted : CollaborationMessageProvenance.ProviderObserved,
            new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero).AddSeconds(index));
    }

    /// <summary>Appends messages through the Domain factory, one save each, so sequences are strictly increasing.</summary>
    private async Task<List<CollaborationMessage>> AppendAsync(
        Guid runId, int count, CollaborationMessageType type = CollaborationMessageType.Proposal)
    {
        await using var dbContext = _fixture.CreateContext();
        var messages = new List<CollaborationMessage>();
        for (var index = 0; index < count; index++)
        {
            var message = Build(runId, type, index);
            dbContext.CollaborationMessages.Add(message);
            await dbContext.SaveChangesAsync();
            messages.Add(message);
        }

        return messages;
    }

    private async Task ExecuteAsync(FormattableString sql)
    {
        await using var dbContext = _fixture.CreateContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private Task WriteColumnAsync(string column, string value, Guid id) => ExecuteAsync(column switch
    {
        "Type" => $"UPDATE collaboration_messages SET Type = {value} WHERE Id = {id}",
        "Provenance" => $"UPDATE collaboration_messages SET Provenance = {value} WHERE Id = {id}",
        "ProtocolVersion" => $"UPDATE collaboration_messages SET ProtocolVersion = {value} WHERE Id = {id}",
        "ActorKind" => $"UPDATE collaboration_messages SET ActorKind = {value} WHERE Id = {id}",
        "ActorAgentRole" => $"UPDATE collaboration_messages SET ActorAgentRole = {value} WHERE Id = {id}",
        "ActorAgentProvider" => $"UPDATE collaboration_messages SET ActorAgentProvider = {value} WHERE Id = {id}",
        "RecipientKind" => $"UPDATE collaboration_messages SET RecipientKind = {value} WHERE Id = {id}",
        "RecipientAgentRole" => $"UPDATE collaboration_messages SET RecipientAgentRole = {value} WHERE Id = {id}",
        "RecipientAgentProvider" => $"UPDATE collaboration_messages SET RecipientAgentProvider = {value} WHERE Id = {id}",
        _ => throw new ArgumentOutOfRangeException(nameof(column)),
    });

    private async Task<GetCollaborationHistoryQueryResult> ReadAsync(Guid runId, string? before = null, int? limit = null)
    {
        await using var dbContext = _fixture.CreateContext();
        var result = await new GetCollaborationHistoryQueryHandler(dbContext).HandleAsync(
            new GetCollaborationHistoryQuery(runId, before, limit), CancellationToken.None);
        Assert.True(result.IsSuccess, string.Join(", ", result.IsSuccess ? [] : result.Errors.Select(error => error.Code)));
        return result.Value;
    }

    private static string Text(long sequence) => sequence.ToString(CultureInfo.InvariantCulture);

    public static TheoryData<int?, bool> Limits() => new()
    {
        { null, true }, { 1, true }, { 10, true }, { 20, true }, { 0, false }, { -1, false }, { 21, false }, { 50, false },
        { int.MaxValue, false }, { int.MinValue, false },
    };

    [Theory]
    [MemberData(nameof(Limits))]
    public void The_limit_is_validated_and_never_clamped(int? limit, bool valid)
    {
        var result = new GetCollaborationHistoryQueryValidator().Validate(new GetCollaborationHistoryQuery(Guid.NewGuid(), null, limit));

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            var error = Assert.Single(result.Errors);
            Assert.Equal("validation.invalid_limit", error.ErrorCode);
            if (Math.Abs((long)limit!.Value) >= 100)
            {
                Assert.DoesNotContain(limit.Value.ToString(CultureInfo.InvariantCulture), error.ErrorMessage, StringComparison.Ordinal);
            }
        }
    }

    public static TheoryData<string?, bool> Cursors() => new()
    {
        { null, true }, { "1", true }, { "9007199254740993", true }, { "9223372036854775807", true }, { "", false }, { " ", false },
        { "0", false }, { "-1", false }, { "+1", false }, { "01", false }, { "1.0", false }, { "1e3", false }, { " 12", false },
        { "9223372036854775808", false }, { "99999999999999999999", false }, { "\u0661\u0662", false }, { "abc", false },
    };

    [Theory]
    [MemberData(nameof(Cursors))]
    public void The_cursor_is_only_a_canonical_positive_int64_decimal_and_a_rejection_never_repeats_the_value(string? cursor, bool valid)
    {
        var result = new GetCollaborationHistoryQueryValidator().Validate(new GetCollaborationHistoryQuery(Guid.NewGuid(), cursor, null));

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            var error = Assert.Single(result.Errors);
            Assert.Equal("validation.invalid_cursor", error.ErrorCode);
            if (cursor is { Length: > 3 })
            {
                Assert.DoesNotContain(cursor, error.ErrorMessage, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task An_unknown_run_fails_with_the_stable_not_found_code_and_an_empty_run_is_an_empty_page()
    {
        await using var dbContext = _fixture.CreateContext();
        var missing = await new GetCollaborationHistoryQueryHandler(dbContext).HandleAsync(
            new GetCollaborationHistoryQuery(Guid.NewGuid(), null, null), CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal("runs.not_found", Assert.Single(missing.Errors).Code);

        var empty = await ReadAsync(await SeedRunAsync());
        Assert.Empty(empty.Entries);
        Assert.False(empty.HasMore);
        Assert.Null(empty.NextBeforeSequence);
    }

    [Fact]
    public async Task Pages_descend_by_sequence_with_an_exclusive_cursor_and_never_include_another_run()
    {
        var runId = await SeedRunAsync();
        var other = await SeedRunAsync();
        await AppendAsync(other, 6);
        var mine = await AppendAsync(runId, 25);
        await AppendAsync(other, 6);
        var expected = mine.Select(message => message.Sequence).Reverse().ToList();

        var first = await ReadAsync(runId);
        Assert.Equal(expected.Take(10), first.Entries.Select(entry => entry.Sequence));
        Assert.True(first.HasMore);
        Assert.Equal(expected[9], first.NextBeforeSequence);

        var second = await ReadAsync(runId, Text(expected[9]), 10);
        Assert.Equal(expected.Skip(10).Take(10), second.Entries.Select(entry => entry.Sequence));
        Assert.Equal(expected[19], second.NextBeforeSequence);

        var third = await ReadAsync(runId, Text(expected[19]), 10);
        Assert.Equal(expected.Skip(20), third.Entries.Select(entry => entry.Sequence));
        Assert.False(third.HasMore);
        Assert.Null(third.NextBeforeSequence);

        var ids = mine.Select(message => message.Id).ToHashSet();
        Assert.All(first.Entries.Concat(second.Entries).Concat(third.Entries), entry =>
        {
            Assert.Equal(runId, entry.RunId);
            Assert.Contains(entry.Id, ids);
        });
    }

    [Fact]
    public async Task A_page_that_exactly_fills_the_limit_has_no_more_and_one_older_row_makes_it_have_more()
    {
        var runId = await SeedRunAsync();
        var messages = await AppendAsync(runId, 20);

        var exact = await ReadAsync(runId, limit: 20);
        Assert.Equal(20, exact.Entries.Count);
        Assert.False(exact.HasMore);
        Assert.Null(exact.NextBeforeSequence);

        var short1 = await ReadAsync(runId, limit: 19);
        Assert.True(short1.HasMore);
        Assert.Equal(messages[1].Sequence, short1.NextBeforeSequence);
        Assert.Equal(messages[1].Sequence, short1.Entries[^1].Sequence);
    }

    [Fact]
    public async Task Messages_appended_between_pages_neither_repeat_nor_skip_an_older_message_and_gaps_are_valid()
    {
        var runId = await SeedRunAsync();
        var other = await SeedRunAsync();
        var first = await AppendAsync(runId, 12);
        await AppendAsync(other, 5);
        var second = await AppendAsync(runId, 13);
        var expected = first.Concat(second).Select(message => message.Sequence).Reverse().ToList();

        var page = await ReadAsync(runId, limit: 10);
        var appended = await AppendAsync(runId, 2);
        var older = await ReadAsync(runId, Text(page.NextBeforeSequence!.Value), 10);

        Assert.Equal(expected.Skip(10).Take(10), older.Entries.Select(entry => entry.Sequence));
        var reload = await ReadAsync(runId, limit: 10);
        Assert.Equal(
            appended.Select(message => message.Sequence).Reverse().Concat(expected.Take(8)), reload.Entries.Select(entry => entry.Sequence));
        Assert.True(expected[12] - expected[13] > 1);
    }

    [Fact]
    public async Task Sequences_around_two_to_the_fifty_third_and_the_int64_maximum_page_exactly()
    {
        var runId = await SeedRunAsync();
        var messages = await AppendAsync(runId, 6);
        long[] values = [9007199254740990, 9007199254740991, 9007199254740992, 9007199254740993, 9223372036854775806, long.MaxValue];
        for (var index = 0; index < values.Length; index++)
        {
            await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = {values[index]} WHERE Id = {messages[index].Id}");
        }

        var all = await ReadAsync(runId, limit: 20);
        Assert.Equal(values.Reverse(), all.Entries.Select(entry => entry.Sequence));

        var paged = await ReadAsync(runId, Text(long.MaxValue), 2);
        Assert.Equal([values[4], values[3]], paged.Entries.Select(entry => entry.Sequence));
        Assert.Equal(values[3], paged.NextBeforeSequence);
        Assert.Equal(
            [values[2], values[1], values[0]],
            (await ReadAsync(runId, "9007199254740993", 20)).Entries.Select(entry => entry.Sequence));
        Assert.Empty((await ReadAsync(runId, "9007199254740990")).Entries);
    }

    [Fact]
    public async Task A_page_is_two_indexed_selects_with_database_bounds_that_never_count_offset_write_or_join()
    {
        var runId = await SeedRunAsync();
        await AppendAsync(runId, 14);
        var recorder = new RecordingInterceptor();
        await using var dbContext = _fixture.CreateContext(recorder);
        var handler = new GetCollaborationHistoryQueryHandler(dbContext);

        var first = await handler.HandleAsync(new GetCollaborationHistoryQuery(runId, null, 7), CancellationToken.None);
        var second = await handler.HandleAsync(
            new GetCollaborationHistoryQuery(runId, Text(first.Value.NextBeforeSequence!.Value), 7), CancellationToken.None);

        Assert.Equal(7, first.Value.Entries.Count);
        Assert.Equal(7, second.Value.Entries.Count);
        Assert.Equal(4, recorder.Commands.Count);
        Assert.All(recorder.Commands, command => Assert.StartsWith("SELECT", command.TrimStart(), StringComparison.OrdinalIgnoreCase));
        var pages = recorder.Commands.Where(command => command.Contains("FROM \"collaboration_messages\"", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, pages.Count);
        Assert.All(pages, page =>
        {
            Assert.Contains("\"RunId\" =", page, StringComparison.Ordinal);
            Assert.Contains("ORDER BY", page, StringComparison.Ordinal);
            Assert.Contains("DESC", page, StringComparison.Ordinal);
            Assert.Contains("LIMIT", page, StringComparison.Ordinal);
            Assert.DoesNotContain("OFFSET", page, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("COUNT(", page, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("JOIN", page, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AttemptId", page, StringComparison.Ordinal);
            Assert.DoesNotContain("InReplyToMessageId", page, StringComparison.Ordinal);
            // The two text columns are selected only under the Domain length bounds, never unconditionally.
            // length() of TEXT stops at an embedded NUL, so the guard must bound the stored bytes.
            Assert.Matches(
                @"(?i)CASE\s+WHEN\s+length\(CAST\([^)]*""Summary""\s+AS\s+BLOB\)\)\s*<=\s*1800\s+THEN\s+[^\s,]*""Summary""\s+END", page);
            Assert.Matches(
                @"(?i)CASE\s+WHEN\s+length\(CAST\([^)]*""StructuredContentJson""\s+AS\s+BLOB\)\)\s*<=\s*15000\s+THEN\s+[^\s,]*""StructuredContentJson""\s+END",
                page);
        });
        Assert.DoesNotContain("\"Sequence\" <", pages[0], StringComparison.Ordinal);
        Assert.Contains("\"Sequence\" <", pages[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_history_writes_nothing()
    {
        var runId = await SeedRunAsync();
        await AppendAsync(runId, 12);
        async Task<(int Runs, int Events, int Messages, int Attempts)> Counts()
        {
            await using var dbContext = _fixture.CreateContext();
            return (
                await dbContext.Runs.CountAsync(), await dbContext.Events.CountAsync(), await dbContext.CollaborationMessages.CountAsync(),
                await dbContext.Attempts.CountAsync());
        }

        var before = await Counts();
        await ReadAsync(runId);
        await ReadAsync(runId, "5", 3);

        Assert.Equal(before, await Counts());
    }

    public static TheoryData<CollaborationMessageType> MessageTypes()
    {
        var data = new TheoryData<CollaborationMessageType>();
        foreach (var type in Enum.GetValues<CollaborationMessageType>())
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public async Task Every_current_message_schema_is_available_with_its_exact_validated_fields(CollaborationMessageType type)
    {
        var runId = await SeedRunAsync();
        var message = (await AppendAsync(runId, 1, type))[0];

        var entry = Assert.Single((await ReadAsync(runId)).Entries);

        Assert.True(entry.ContentAvailable);
        Assert.Equal(message.Id, entry.Id);
        Assert.Equal(type.ToString(), entry.Type);
        Assert.Equal("1.0", entry.ProtocolVersion);
        Assert.Equal(message.Provenance.ToString(), entry.Provenance);
        Assert.Equal(message.Summary, entry.Summary);
        Assert.Equal(message.OccurredAtUtc, entry.OccurredAtUtc);
        Assert.Equal(
            CollaborationHistoryFixtureContent.FieldNames[type].Select(name => (name, CollaborationHistoryFixtureContent.Value(name))),
            entry.Fields.Select(field => (field.Name, field.Value)));
        Assert.Equal(ParticipantKind.Orchestrator, entry.Recipient.Kind);
        Assert.Equal(type == CollaborationMessageType.HumanInstruction ? ParticipantKind.Human : ParticipantKind.Agent, entry.Actor.Kind);
    }

    [Fact]
    public async Task Valid_unknown_role_agent_participants_are_preserved()
    {
        var runId = await SeedRunAsync();
        await using (var dbContext = _fixture.CreateContext())
        {
            dbContext.CollaborationMessages.Add(CollaborationMessage.Record(
                Guid.NewGuid(), runId, null, "1.0", ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Question, Guid.NewGuid(),
                "Simulated question.", CollaborationHistoryFixtureContent.Json(CollaborationMessageType.Question),
                CollaborationMessageProvenance.Simulated, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        var entry = Assert.Single((await ReadAsync(runId)).Entries);

        Assert.True(entry.ContentAvailable);
        Assert.Equal(new CollaborationHistoryParticipant(ParticipantKind.Agent, null, AgentProvider.ClaudeCode), entry.Actor);
        Assert.Equal(new CollaborationHistoryParticipant(ParticipantKind.Agent, null, AgentProvider.Codex), entry.Recipient);
        Assert.Equal("Simulated", entry.Provenance);
    }

    [Theory]
    [InlineData("Type", "Overlord")]
    [InlineData("Type", "proposal")]
    [InlineData("Provenance", "Forged")]
    [InlineData("ProtocolVersion", "2.0")]
    [InlineData("ProtocolVersion", "1.0 ")]
    [InlineData("ActorKind", "Wizard")]
    [InlineData("ActorAgentRole", "Overlord")]
    [InlineData("ActorAgentProvider", "Gemini")]
    [InlineData("RecipientKind", "Wizard")]
    [InlineData("RecipientAgentRole", "Overlord")]
    [InlineData("RecipientAgentProvider", "Gemini")]
    public async Task Unknown_stored_metadata_keeps_its_header_as_unavailable_without_exposing_it_and_neighbors_stay_healthy(
        string column, string stored)
    {
        var runId = await SeedRunAsync();
        var messages = await AppendAsync(runId, 3);
        await WriteColumnAsync(column, stored + Secret, messages[1].Id);

        var page = await ReadAsync(runId);

        Assert.Equal(3, page.Entries.Count);
        Assert.Equal([true, false, true], page.Entries.Select(entry => entry.ContentAvailable).Reverse());
        var damaged = page.Entries.Single(entry => entry.Id == messages[1].Id);
        Assert.Null(damaged.Summary);
        Assert.Empty(damaged.Fields);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(page), StringComparison.Ordinal);
        Assert.DoesNotContain("Overlord", JsonSerializer.Serialize(page), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Incoherent_participants_and_provenance_make_only_that_content_unavailable()
    {
        var runId = await SeedRunAsync();
        var messages = await AppendAsync(runId, 6);
        await ExecuteAsync($"UPDATE collaboration_messages SET RecipientKind = 'Agent', RecipientAgentRole = 'Planner', RecipientAgentProvider = 'Codex' WHERE Id = {messages[0].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET ActorKind = 'None', ActorAgentRole = NULL, ActorAgentProvider = NULL WHERE Id = {messages[1].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET ActorKind = 'Human', ActorAgentRole = 'Planner' WHERE Id = {messages[2].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET Provenance = 'HostConstructed' WHERE Id = {messages[3].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET ActorAgentRole = NULL WHERE Id = {messages[4].Id}");

        var page = await ReadAsync(runId);

        var byId = page.Entries.ToDictionary(entry => entry.Id);
        Assert.False(byId[messages[0].Id].ContentAvailable);
        Assert.False(byId[messages[1].Id].ContentAvailable);
        Assert.Null(byId[messages[1].Id].Actor.Kind);
        Assert.False(byId[messages[2].Id].ContentAvailable);
        Assert.Null(byId[messages[2].Id].Actor.Kind);
        Assert.False(byId[messages[3].Id].ContentAvailable);
        Assert.False(byId[messages[4].Id].ContentAvailable);
        Assert.True(byId[messages[5].Id].ContentAvailable);
        Assert.All(page.Entries.Where(entry => !entry.ContentAvailable), entry =>
        {
            Assert.Null(entry.Summary);
            Assert.Empty(entry.Fields);
        });
    }

    public static TheoryData<string, string> DefectiveContent() => new()
    {
        { "summary", "" },
        { "summary", "   " },
        { "summary", "The password is hunter2 " + Secret },
        { "summary", "Look at C:\\Users\\x " + Secret },
        { "summary", new string('s', 601) },
        { "summary", string.Concat(Enumerable.Repeat("\U0001F600", 600)) },
        { "json", "not json " + Secret },
        { "json", "[\"" + Secret + "\"]" },
        { "json", "{\"scope\":\"" + Secret + "\"}" },
        { "json", "{\"scope\":\"a\",\"implementationSteps\":\"b\",\"risks\":\"c\",\"verificationPlan\":\"d\",\"escalationPoints\":\"e\",\"extra\":\"" + Secret + "\"}" },
        { "json", "{\"scope\":\"a\",\"scope\":\"" + Secret + "\",\"implementationSteps\":\"b\",\"risks\":\"c\",\"verificationPlan\":\"d\",\"escalationPoints\":\"e\"}" },
        { "json", "{\"scope\":1,\"implementationSteps\":\"b\",\"risks\":\"c\",\"verificationPlan\":\"d\",\"escalationPoints\":\"e\"}" },
        { "json", "{\"scope\":\"\",\"implementationSteps\":\"b\",\"risks\":\"c\",\"verificationPlan\":\"d\",\"escalationPoints\":\"e\"}" },
        { "json", "{\"scope\":\"api key " + Secret + "\",\"implementationSteps\":\"b\",\"risks\":\"c\",\"verificationPlan\":\"d\",\"escalationPoints\":\"e\"}" },
        { "json", "{\"scope\":\"" + new string('v', 901) + "\",\"implementationSteps\":\"b\",\"risks\":\"c\",\"verificationPlan\":\"d\",\"escalationPoints\":\"e\"}" },
        { "json", "{\"scope\":\"a\"}" },
        { "json", "{}" },
        { "json", "" },
        { "json", "{\"scope\":\"" + new string('x', 5000) + "\"}" },
    };

    [Theory]
    [MemberData(nameof(DefectiveContent))]
    public async Task Malformed_unsafe_or_oversized_content_is_unavailable_while_its_neighbors_stay_healthy(string column, string stored)
    {
        var runId = await SeedRunAsync();
        var messages = await AppendAsync(runId, 3);
        var victim = messages[1].Id;
        if (column == "summary")
        {
            await ExecuteAsync($"UPDATE collaboration_messages SET Summary = {stored} WHERE Id = {victim}");
        }
        else
        {
            await ExecuteAsync($"UPDATE collaboration_messages SET StructuredContentJson = {stored} WHERE Id = {victim}");
        }

        var page = await ReadAsync(runId);

        Assert.Equal([true, false, true], page.Entries.Select(entry => entry.ContentAvailable).Reverse());
        var damaged = page.Entries.Single(entry => entry.Id == victim);
        Assert.Equal("Proposal", damaged.Type);
        Assert.Null(damaged.Summary);
        Assert.Empty(damaged.Fields);
        var serialized = JsonSerializer.Serialize(page);
        Assert.DoesNotContain(Secret, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_text_columns_are_not_selected_at_all()
    {
        var runId = await SeedRunAsync();
        var messages = await AppendAsync(runId, 2);
        await ExecuteAsync($"UPDATE collaboration_messages SET Summary = {new string('s', 1_000_000)}, StructuredContentJson = {new string('j', 2_000_000)} WHERE Id = {messages[0].Id}");
        var recorder = new RecordingInterceptor();
        await using var dbContext = _fixture.CreateContext(recorder);

        var result = await new GetCollaborationHistoryQueryHandler(dbContext).HandleAsync(
            new GetCollaborationHistoryQuery(runId, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([true, false], result.Value.Entries.Select(entry => entry.ContentAvailable));
        Assert.Equal(1, result.Value.Entries.Count(entry => entry.ContentAvailable));
        Assert.True(JsonSerializer.Serialize(result.Value).Length < 10_000);
    }

    // An ordinary string parameter can truncate at an embedded NUL, so the oversized text is stored from UTF-8 bytes cast to TEXT.
    // SQLite length() on TEXT stops at the first NUL, which a character-count guard alone would not bound.
    private async Task StoreNulTextAsync(Guid id, string? summary, string? content)
    {
        await using var dbContext = _fixture.CreateContext();
        if (summary is not null)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(summary);
            await dbContext.Database.ExecuteSqlRawAsync("UPDATE collaboration_messages SET Summary = CAST({0} AS TEXT) WHERE Id = {1}", bytes, id);
        }

        if (content is not null)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            await dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE collaboration_messages SET StructuredContentJson = CAST({0} AS TEXT) WHERE Id = {1}", bytes, id);
        }
    }

    [Fact]
    public async Task Oversized_text_hidden_behind_an_embedded_nul_is_not_returned_and_its_content_stays_unavailable()
    {
        var runId = await SeedRunAsync();
        var messages = await AppendAsync(runId, 4);
        var validContent = CollaborationHistoryFixtureContent.Json(CollaborationMessageType.Proposal);
        await StoreNulTextAsync(messages[1].Id, "Safe summary\0" + new string('s', 1_000_000) + Secret, validContent + "\0" + new string('j', 2_000_000) + Secret);
        await StoreNulTextAsync(messages[2].Id, null, validContent + "\0" + new string('j', 2_000_000) + Secret);
        await StoreNulTextAsync(messages[3].Id, "Safe summary\0" + new string('s', 1_000_000) + Secret, null);
        var recorder = new RecordingInterceptor();
        await using var dbContext = _fixture.CreateContext(recorder);

        var result = await new GetCollaborationHistoryQueryHandler(dbContext).HandleAsync(
            new GetCollaborationHistoryQuery(runId, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([false, false, false, true], result.Value.Entries.Select(entry => entry.ContentAvailable));
        Assert.All(result.Value.Entries.Where(entry => !entry.ContentAvailable), entry =>
        {
            Assert.Null(entry.Summary);
            Assert.Empty(entry.Fields);
        });
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result.Value), StringComparison.Ordinal);

        // The projection itself: the exact emitted statement, run again, returns neither oversized column for the damaged rows.
        var page = recorder.Reads.Single(read => read.Text.Contains("FROM \"collaboration_messages\"", StringComparison.Ordinal));
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbContext.Database.GetDbConnection().ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // the statement is the handler's own, replayed with its own parameters
        command.CommandText = page.Text;
#pragma warning restore CA2100
        foreach (var (name, value) in page.Parameters)
        {
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter(name, value ?? DBNull.Value));
        }

        await using var reader = await command.ExecuteReaderAsync();
        var seen = new Dictionary<long, (string? Summary, string? Json)>();
        while (await reader.ReadAsync())
        {
            seen[reader.GetInt64(0)] = (reader.IsDBNull(12) ? null : reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetString(13));
        }

        Assert.Equal(4, seen.Count);
        foreach (var damaged in new[] { messages[1], messages[2], messages[3] })
        {
            var (summary, json) = seen[damaged.Sequence];
            Assert.True((summary?.Length ?? 0) <= CollaborationMessageContentPolicy.MaximumSummaryLength, $"summary of {damaged.Sequence}");
            Assert.True((json?.Length ?? 0) <= CollaborationMessageContentPolicy.MaximumStructuredContentLength, $"json of {damaged.Sequence}");
        }

        Assert.Null(seen[messages[1].Sequence].Summary);
        Assert.Null(seen[messages[1].Sequence].Json);
        Assert.Null(seen[messages[2].Sequence].Json);
        Assert.Null(seen[messages[3].Sequence].Summary);
        Assert.NotNull(seen[messages[0].Sequence].Summary);
        Assert.NotNull(seen[messages[0].Sequence].Json);
    }

    [Theory]
    [InlineData("empty-identity")]
    [InlineData("zero-sequence")]
    [InlineData("negative-sequence")]
    public async Task A_selected_row_without_an_identity_or_a_positive_sequence_makes_the_whole_page_unavailable(string defect)
    {
        var runId = await SeedRunAsync();
        var messages = await AppendAsync(runId, 5);
        var victim = messages[0];
        switch (defect)
        {
            case "empty-identity":
                await ExecuteAsync($"UPDATE collaboration_messages SET Id = '00000000-0000-0000-0000-000000000000' WHERE Id = {victim.Id}");
                break;
            case "zero-sequence":
                await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = 0 WHERE Id = {victim.Id}");
                break;
            default:
                await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = -7 WHERE Id = {victim.Id}");
                break;
        }

        await using var dbContext = _fixture.CreateContext();
        var handler = new GetCollaborationHistoryQueryHandler(dbContext);

        var full = await handler.HandleAsync(new GetCollaborationHistoryQuery(runId, null, 5), CancellationToken.None);
        var extra = await handler.HandleAsync(new GetCollaborationHistoryQuery(runId, null, 4), CancellationToken.None);
        var clear = await handler.HandleAsync(new GetCollaborationHistoryQuery(runId, null, 3), CancellationToken.None);

        foreach (var failed in new[] { full, extra })
        {
            Assert.True(failed.IsFailure);
            var error = Assert.Single(failed.Errors);
            Assert.Equal("collaboration_history.unavailable", error.Code);
            Assert.Equal(Devalente.Shared.Results.ErrorType.Conflict, error.Type);
        }

        Assert.True(clear.IsSuccess);
        Assert.Equal(3, clear.Value.Entries.Count);
    }
}
