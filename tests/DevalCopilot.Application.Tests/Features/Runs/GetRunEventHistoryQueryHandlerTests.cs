using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Queries.GetRunEventHistory;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0034 at the query boundary, over real file-backed SQLite with the production migrations: the structural bounds are refused
/// rather than clamped, the sequence cursor is exact and exclusive, the page is a fixed read-only indexed header select that never
/// reads a payload, and unknown stored values keep their rows and are disclosed only as unrecognized.
/// </summary>
public sealed class GetRunEventHistoryQueryHandlerTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class RecordingInterceptor : DbCommandInterceptor
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

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
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

    private async Task<List<RunEvent>> AppendAsync(Guid runId, int count, string payload = "{}")
    {
        await using var dbContext = _fixture.CreateContext();
        var events = new List<RunEvent>();
        for (var index = 0; index < count; index++)
        {
            var runEvent = RunEvent.Record(
                Guid.NewGuid(), runId, null, RunEventType.CollaborationMessageRecorded, ParticipantIdentity.ForOrchestrator(), payload,
                new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero).AddSeconds(index));
            dbContext.Events.Add(runEvent);
            events.Add(runEvent);
        }

        await dbContext.SaveChangesAsync();
        return events;
    }

    private async Task ExecuteAsync(FormattableString sql)
    {
        await using var dbContext = _fixture.CreateContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private async Task<GetRunEventHistoryQueryResult> ReadAsync(Guid runId, string? before = null, int? limit = null)
    {
        await using var dbContext = _fixture.CreateContext();
        var result = await new GetRunEventHistoryQueryHandler(dbContext).HandleAsync(
            new GetRunEventHistoryQuery(runId, before, limit), CancellationToken.None);
        Assert.True(result.IsSuccess, string.Join(", ", result.IsSuccess ? [] : result.Errors.Select(error => error.Code)));
        return result.Value;
    }

    public static TheoryData<int?, bool> Limits() => new()
    {
        { null, true }, { 1, true }, { 20, true }, { 50, true }, { 0, false }, { -1, false }, { 51, false }, { 100, false },
        { int.MaxValue, false }, { int.MinValue, false },
    };

    [Theory]
    [MemberData(nameof(Limits))]
    public void The_limit_is_validated_and_never_clamped(int? limit, bool valid)
    {
        var result = new GetRunEventHistoryQueryValidator().Validate(new GetRunEventHistoryQuery(Guid.NewGuid(), null, limit));

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            var error = Assert.Single(result.Errors);
            Assert.Equal("validation.invalid_limit", error.ErrorCode);
            if (Math.Abs((long)limit!.Value) >= 100)
            {
                Assert.DoesNotContain(limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), error.ErrorMessage, StringComparison.Ordinal);
            }
        }
    }

    public static TheoryData<string?, bool> Cursors() => new()
    {
        { null, true }, { "1", true }, { "9", true }, { "10", true }, { "9007199254740991", true }, { "9007199254740992", true },
        { "9007199254740993", true }, { "9223372036854775806", true }, { "9223372036854775807", true },
        { "", false }, { " ", false }, { "0", false }, { "00", false }, { "01", false }, { "-1", false }, { "+1", false },
        { " 1", false }, { "1 ", false }, { "1.0", false }, { "1e3", false }, { "1,000", false }, { "0x10", false }, { "abc", false },
        { "9223372036854775808", false }, { "99999999999999999999", false }, { "18446744073709551616", false },
        { "\uFF11\uFF12", false }, { "\u0661\u0662", false }, { "1\n", false }, { "1_000", false }, { "NaN", false }, { "Infinity", false },
    };

    [Theory]
    [MemberData(nameof(Cursors))]
    public void The_cursor_is_only_a_canonical_positive_int64_decimal_and_a_rejection_never_repeats_the_value(string? cursor, bool valid)
    {
        var result = new GetRunEventHistoryQueryValidator().Validate(new GetRunEventHistoryQuery(Guid.NewGuid(), cursor, null));

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            var error = Assert.Single(result.Errors);
            Assert.Equal("validation.invalid_cursor", error.ErrorCode);
            Assert.Equal("BeforeSequence", error.PropertyName);
            if (cursor?.Trim().Length >= 3)
            {
                Assert.DoesNotContain(cursor, error.ErrorMessage, StringComparison.Ordinal);
            }
        }

        Assert.Equal(valid && cursor is not null, RunEventHistoryCursor.TryParse(cursor, out var parsed));
        if (valid && cursor is not null)
        {
            Assert.Equal(cursor, RunEventHistoryCursor.Format(parsed));
        }
    }

    [Fact]
    public async Task An_unknown_run_fails_with_the_stable_not_found_code_after_one_read()
    {
        var recorder = new RecordingInterceptor();
        await using var dbContext = _fixture.CreateContext(recorder);

        var result = await new GetRunEventHistoryQueryHandler(dbContext).HandleAsync(
            new GetRunEventHistoryQuery(Guid.NewGuid(), null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_found", result.Errors[0].Code);
        Assert.Single(recorder.Commands);
    }

    [Fact]
    public async Task A_run_without_events_is_an_empty_page_without_a_cursor()
    {
        var runId = await SeedRunAsync();

        var page = await ReadAsync(runId);

        Assert.Equal(runId, page.RunId);
        Assert.Empty(page.Entries);
        Assert.False(page.HasMore);
        Assert.Null(page.NextBeforeSequence);
    }

    [Fact]
    public async Task Pages_descend_by_sequence_with_an_exclusive_cursor_and_never_include_another_run()
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        await AppendAsync(otherRun, 9);
        var mine = await AppendAsync(runId, 25);
        await AppendAsync(otherRun, 9);
        var expected = mine.Select(runEvent => runEvent.Sequence).OrderDescending().ToList();

        var first = await ReadAsync(runId, limit: 10);
        Assert.Equal(expected.Take(10), first.Entries.Select(entry => entry.Sequence));
        Assert.True(first.HasMore);
        Assert.Equal(expected[9], first.NextBeforeSequence);

        var second = await ReadAsync(runId, RunEventHistoryCursor.Format(first.NextBeforeSequence!.Value), 10);
        Assert.Equal(expected.Skip(10).Take(10), second.Entries.Select(entry => entry.Sequence));
        Assert.True(second.HasMore);
        Assert.DoesNotContain(expected[9], second.Entries.Select(entry => entry.Sequence));

        var third = await ReadAsync(runId, RunEventHistoryCursor.Format(second.NextBeforeSequence!.Value), 10);
        Assert.Equal(expected.Skip(20), third.Entries.Select(entry => entry.Sequence));
        Assert.False(third.HasMore);
        Assert.Null(third.NextBeforeSequence);

        Assert.All(first.Entries.Concat(second.Entries).Concat(third.Entries), entry =>
        {
            Assert.Equal(runId, entry.RunId);
            Assert.Contains(entry.Id, mine.Select(runEvent => runEvent.Id));
        });
    }

    [Fact]
    public async Task A_page_that_exactly_fills_the_limit_has_no_more_and_one_older_row_makes_it_have_more()
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 20);

        var exact = await ReadAsync(runId, limit: 20);
        Assert.Equal(20, exact.Entries.Count);
        Assert.False(exact.HasMore);
        Assert.Null(exact.NextBeforeSequence);

        var one = await ReadAsync(runId, limit: 1);
        Assert.Equal([events[^1].Sequence], one.Entries.Select(entry => entry.Sequence));
        Assert.True(one.HasMore);
        Assert.Equal(events[^1].Sequence, one.NextBeforeSequence);

        var defaultPage = await ReadAsync(runId);
        Assert.Equal(20, defaultPage.Entries.Count);
        await AppendAsync(runId, 1);
        var overflow = await ReadAsync(runId);
        Assert.Equal(20, overflow.Entries.Count);
        Assert.True(overflow.HasMore);

        var beforeOldest = await ReadAsync(runId, RunEventHistoryCursor.Format(events[0].Sequence));
        Assert.Empty(beforeOldest.Entries);
        Assert.False(beforeOldest.HasMore);
    }

    [Fact]
    public async Task Events_appended_between_pages_neither_repeat_nor_skip_an_older_header()
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 25);
        var expected = events.Select(runEvent => runEvent.Sequence).OrderDescending().ToList();

        var first = await ReadAsync(runId, limit: 10);
        await AppendAsync(runId, 3);
        var second = await ReadAsync(runId, RunEventHistoryCursor.Format(first.NextBeforeSequence!.Value), 10);

        Assert.Equal(expected.Skip(10).Take(10), second.Entries.Select(entry => entry.Sequence));
        var fresh = await ReadAsync(runId, limit: 10);
        Assert.Equal(3, fresh.Entries.Count(entry => entry.Sequence > expected[0]));
        Assert.All(fresh.Entries.Take(3), entry => Assert.True(entry.Sequence > expected[0]));
        Assert.Equal(expected.Take(7), fresh.Entries.Skip(3).Select(entry => entry.Sequence));
    }

    [Fact]
    public async Task Sequence_gaps_between_a_runs_events_are_valid_and_timestamps_do_not_define_order()
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        var mine = await AppendAsync(runId, 4);
        await AppendAsync(otherRun, 5);
        await AppendAsync(runId, 3);

        var page = await ReadAsync(runId, limit: 50);

        var sequences = page.Entries.Select(entry => entry.Sequence).ToList();
        Assert.Equal(7, sequences.Count);
        Assert.Equal(sequences.OrderDescending(), sequences);
        Assert.True(sequences[2] - sequences[3] > 1, "The other run's rows must leave a gap inside this run's sequence.");
        Assert.Equal(mine.Select(runEvent => runEvent.Id), page.Entries.Skip(3).Select(entry => entry.Id).Reverse());

        // Reverse the recorded times: only the sequence orders the page.
        await ExecuteAsync($"UPDATE events SET OccurredAtUtc = '2000-01-01 00:00:00+00:00' WHERE RunId = {runId} AND Sequence = {sequences[0]}");
        var again = await ReadAsync(runId, limit: 50);
        Assert.Equal(sequences, again.Entries.Select(entry => entry.Sequence));
    }

    [Fact]
    public async Task Sequences_around_two_to_the_fifty_third_and_the_int64_maximum_page_exactly()
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 6);
        long[] values = [9007199254740990, 9007199254740991, 9007199254740992, 9007199254740993, 9223372036854775806, long.MaxValue];
        for (var index = 0; index < values.Length; index++)
        {
            var (id, value) = (events[index].Id, values[index]);
            await ExecuteAsync($"UPDATE events SET Sequence = {value} WHERE Id = {id}");
        }

        var all = await ReadAsync(runId, limit: 50);
        Assert.Equal(values.OrderDescending(), all.Entries.Select(entry => entry.Sequence));

        var belowMax = await ReadAsync(runId, "9223372036854775807", 2);
        Assert.Equal([9223372036854775806, 9007199254740993], belowMax.Entries.Select(entry => entry.Sequence));
        Assert.True(belowMax.HasMore);
        Assert.Equal(9007199254740993, belowMax.NextBeforeSequence);

        // The neighbors of 2^53 are distinguishable: a floating-point cursor would collapse 2^53 + 1 onto 2^53.
        var belowOdd = await ReadAsync(runId, "9007199254740993", 50);
        Assert.Equal([9007199254740992, 9007199254740991, 9007199254740990], belowOdd.Entries.Select(entry => entry.Sequence));
        var belowEven = await ReadAsync(runId, "9007199254740992", 50);
        Assert.Equal([9007199254740991, 9007199254740990], belowEven.Entries.Select(entry => entry.Sequence));
        var belowFirst = await ReadAsync(runId, "9007199254740990", 50);
        Assert.Empty(belowFirst.Entries);
    }

    [Fact]
    public async Task A_page_is_two_indexed_header_selects_that_never_count_offset_write_or_read_a_payload()
    {
        var runId = await SeedRunAsync();
        await AppendAsync(runId, 30, payload: "{\"secret\":\"SECRET-SENTINEL-8f2c\"");
        var recorder = new RecordingInterceptor();
        await using var dbContext = _fixture.CreateContext(recorder);
        var handler = new GetRunEventHistoryQueryHandler(dbContext);

        var first = await handler.HandleAsync(new GetRunEventHistoryQuery(runId, null, 7), CancellationToken.None);
        var second = await handler.HandleAsync(
            new GetRunEventHistoryQuery(runId, RunEventHistoryCursor.Format(first.Value.NextBeforeSequence!.Value), 7), CancellationToken.None);

        Assert.Equal(7, first.Value.Entries.Count);
        Assert.Equal(7, second.Value.Entries.Count);
        // Run existence and the header page, twice; every one a SELECT.
        Assert.Equal(4, recorder.Commands.Count);
        Assert.All(recorder.Commands, command => Assert.StartsWith("SELECT", command.TrimStart(), StringComparison.OrdinalIgnoreCase));
        var pages = recorder.Commands.Where(command => command.Contains("FROM \"events\"", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, pages.Count);
        Assert.All(pages, page =>
        {
            Assert.Contains("\"RunId\" =", page, StringComparison.Ordinal);
            Assert.Contains("ORDER BY", page, StringComparison.Ordinal);
            Assert.Contains("DESC", page, StringComparison.Ordinal);
            Assert.Contains("LIMIT", page, StringComparison.Ordinal);
            Assert.DoesNotContain("OFFSET", page, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("COUNT(", page, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PayloadJson", page, StringComparison.Ordinal);
            Assert.DoesNotContain("AttemptId", page, StringComparison.Ordinal);
            Assert.DoesNotContain("JOIN", page, StringComparison.OrdinalIgnoreCase);
        });
        Assert.DoesNotContain("\"Sequence\" <", pages[0], StringComparison.Ordinal);
        Assert.Contains("\"Sequence\" <", pages[1], StringComparison.Ordinal);
        var serialized = JsonSerializer.Serialize(new[] { first.Value, second.Value });
        Assert.DoesNotContain("SECRET-SENTINEL", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_history_writes_nothing()
    {
        var runId = await SeedRunAsync();
        await AppendAsync(runId, 12);
        async Task<(int Runs, int Events, int Attempts)> Counts()
        {
            await using var dbContext = _fixture.CreateContext();
            return (await dbContext.Runs.CountAsync(), await dbContext.Events.CountAsync(), await dbContext.Attempts.CountAsync());
        }

        var before = await Counts();
        await ReadAsync(runId);
        await ReadAsync(runId, "5", 3);

        Assert.Equal(before, await Counts());
    }

    [Fact]
    public async Task Every_known_event_type_is_disclosed_exactly_and_the_allow_list_names_every_constant()
    {
        var constants = typeof(RunEventType).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();
        Assert.Equal(constants.Order(StringComparer.Ordinal), RunEventHistoryEventTypes.Known.Order(StringComparer.Ordinal));

        // Pinned on purpose: a new RunEventType constant is admitted by the operation automatically, but the cockpit's closed label set
        // must learn it first (the frontend pins this same list), so adding one has to update both pinned lists deliberately.
        Assert.Equal(
            [
                "run.started", "codex.proposal", "claude.challenge", "codex.resolution", "claude.execution", "run.completed",
                "collaboration.message_recorded", "process.output_captured", "process.ended_without_output", "agent.attempt_dispatched",
                "agent.attempt_completed", "agent.output_recovered", "run.codex_assignment_preference_changed",
                "run.claude_model_preference_changed", "run.claude_mutation_turn_limit_changed", "run.token_warning_threshold_changed",
                "run.token_stop_threshold_changed", "run.codex_account_usage_stop_changed", "run.codex_account_usage_warning_changed",
                "local_commit.admitted", "local_commit.executing", "local_commit.completed", "local_commit.failed",
                "local_commit.interrupted", "local_commit.needs_attention", "run.abandoned",
                "run.github_observation_target_recorded", "run.github_repository_identity_bound",
                "run.github_branch_publication_approved",
                "planning_sequence.armed", "planning_sequence.step_reserved", "planning_sequence.step_claimed",
                "planning_sequence.step_concluded", "planning_sequence.stop_requested", "planning_sequence.ended",
                "agent_supervision.registered", "agent_supervision.phase_entered", "agent_supervision.attention", "agent_supervision.closed",
            ],
            constants);

        var runId = await SeedRunAsync();
        await using (var dbContext = _fixture.CreateContext())
        {
            foreach (var type in constants)
            {
                dbContext.Events.Add(RunEvent.Record(Guid.NewGuid(), runId, null, type, ParticipantIdentity.ForOrchestrator(), "{}", DateTimeOffset.UtcNow));
            }

            await dbContext.SaveChangesAsync();
        }

        var page = await ReadAsync(runId, limit: 50);

        Assert.Equal(constants, page.Entries.Select(entry => entry.EventType).Reverse());
    }

    [Theory]
    [InlineData("run.unknown")]
    [InlineData("RUN.STARTED")]
    [InlineData("Run.Started")]
    [InlineData("run.started ")]
    [InlineData(" run.started")]
    [InlineData("run.started\n")]
    [InlineData("")]
    [InlineData("run.started\u0000secret")]
    [InlineData("SECRET-EVENT-TYPE-3d91")]
    public async Task An_unknown_stored_event_type_keeps_its_row_is_never_exposed_and_never_promotes_a_neighbor(string stored)
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 3);
        await ExecuteAsync($"UPDATE events SET EventType = {stored} WHERE Id = {events[1].Id}");

        var page = await ReadAsync(runId);

        Assert.Equal(events.Select(runEvent => runEvent.Id).Reverse(), page.Entries.Select(entry => entry.Id));
        Assert.Equal(
            [RunEventType.CollaborationMessageRecorded, null, RunEventType.CollaborationMessageRecorded],
            page.Entries.Select(entry => entry.EventType));
        Assert.DoesNotContain("SECRET-EVENT-TYPE", JsonSerializer.Serialize(page), StringComparison.Ordinal);
    }

    public static TheoryData<string, string?, string?, ParticipantKind?, AgentRole?, AgentProvider?> Actors() => new()
    {
        { "None", null, null, ParticipantKind.None, null, null },
        { "Orchestrator", null, null, ParticipantKind.Orchestrator, null, null },
        { "Human", null, null, ParticipantKind.Human, null, null },
        { "Agent", "Planner", "Codex", ParticipantKind.Agent, AgentRole.Planner, AgentProvider.Codex },
        { "Agent", "CriticalReviewer", "ClaudeCode", ParticipantKind.Agent, AgentRole.CriticalReviewer, AgentProvider.ClaudeCode },
        { "Agent", "Resolver", "Codex", ParticipantKind.Agent, AgentRole.Resolver, AgentProvider.Codex },
        { "Agent", "Implementer", "ClaudeCode", ParticipantKind.Agent, AgentRole.Implementer, AgentProvider.ClaudeCode },
        { "Agent", "CodeReviewer", "Codex", ParticipantKind.Agent, AgentRole.CodeReviewer, AgentProvider.Codex },
        // A valid Agent whose role was never known is preserved exactly as recorded.
        { "Agent", null, "Codex", ParticipantKind.Agent, null, AgentProvider.Codex },
        { "Agent", null, "ClaudeCode", ParticipantKind.Agent, null, AgentProvider.ClaudeCode },
        // Unknown or inconsistent combinations are unrecognized, with no role or provider.
        { "Agent", "Planner", null, null, null, null },
        { "Agent", null, null, null, null, null },
        { "Agent", "Janitor", "Codex", null, null, null },
        { "Agent", "Planner", "Gemini", null, null, null },
        { "Agent", null, "Gemini", null, null, null },
        { "Agent", "", "Codex", null, null, null },
        { "Agent", "planner", "Codex", null, null, null },
        { "Agent", "Planner", "codex", null, null, null },
        { "None", "Planner", null, null, null, null },
        { "None", null, "Codex", null, null, null },
        { "Human", "Planner", "Codex", null, null, null },
        { "Orchestrator", null, "ClaudeCode", null, null, null },
        { "Orchestrator", "Resolver", null, null, null, null },
        { "Robot", null, null, null, null, null },
        { "human", null, null, null, null, null },
        { "", null, null, null, null, null },
        { "2", null, null, null, null, null },
    };

    [Theory]
    [MemberData(nameof(Actors))]
    public async Task Actor_columns_are_classified_without_materializing_unknown_values_and_neighbors_stay_healthy(
        string kind, string? role, string? provider, ParticipantKind? expectedKind, AgentRole? expectedRole, AgentProvider? expectedProvider)
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 3);
        await ExecuteAsync(
            $"UPDATE events SET ActorKind = {kind}, ActorAgentRole = {role}, ActorAgentProvider = {provider} WHERE Id = {events[1].Id}");

        var page = await ReadAsync(runId);

        Assert.Equal(events.Select(runEvent => runEvent.Id).Reverse(), page.Entries.Select(entry => entry.Id));
        Assert.Equal(new RunEventHistoryActor(expectedKind, expectedRole, expectedProvider), page.Entries[1].Actor);
        var healthy = new RunEventHistoryActor(ParticipantKind.Orchestrator, null, null);
        Assert.Equal(healthy, page.Entries[0].Actor);
        Assert.Equal(healthy, page.Entries[2].Actor);
    }

    [Fact]
    public async Task The_actor_classification_agrees_with_the_domain_identity_for_every_known_combination()
    {
        var runId = await SeedRunAsync();
        var kinds = Enum.GetValues<ParticipantKind>();
        var roles = new AgentRole?[] { null }.Concat(Enum.GetValues<AgentRole>().Select(role => (AgentRole?)role)).ToArray();
        var providers = new AgentProvider?[] { null }.Concat(Enum.GetValues<AgentProvider>().Select(provider => (AgentProvider?)provider)).ToArray();
        var combinations = (from kind in kinds from role in roles from provider in providers select (kind, role, provider)).ToList();
        var events = await AppendAsync(runId, combinations.Count);
        for (var index = 0; index < combinations.Count; index++)
        {
            var (kind, role, provider) = combinations[index];
            var (id, kindText, roleText, providerText) = (events[index].Id, kind.ToString(), role?.ToString(), provider?.ToString());
            await ExecuteAsync(
                $"UPDATE events SET ActorKind = {kindText}, ActorAgentRole = {roleText}, ActorAgentProvider = {providerText} WHERE Id = {id}");
        }

        var newest = await ReadAsync(runId, limit: 50);
        var rest = await ReadAsync(runId, RunEventHistoryCursor.Format(newest.NextBeforeSequence!.Value), 50);
        var page = new { Entries = newest.Entries.Concat(rest.Entries).ToList() };

        Assert.Equal(combinations.Count, page.Entries.Count);
        Assert.False(rest.HasMore);
        for (var index = 0; index < combinations.Count; index++)
        {
            var (kind, role, provider) = combinations[index];
            var valid = true;
            try
            {
                ParticipantIdentity.FromParts(kind, role, provider);
            }
            catch (ArgumentException)
            {
                valid = false;
            }

            var actor = page.Entries[combinations.Count - 1 - index].Actor;
            Assert.Equal(valid ? new RunEventHistoryActor(kind, role, provider) : new RunEventHistoryActor(null, null, null), actor);
        }
    }

    [Theory]
    [InlineData("empty-identity")]
    [InlineData("zero-sequence")]
    [InlineData("negative-sequence")]
    public async Task A_selected_header_without_an_identity_or_a_positive_sequence_makes_the_whole_page_unavailable(string defect)
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 5);
        var victim = events[0];
        switch (defect)
        {
            case "empty-identity":
                await ExecuteAsync($"UPDATE events SET Id = '00000000-0000-0000-0000-000000000000' WHERE Id = {victim.Id}");
                break;
            case "zero-sequence":
                await ExecuteAsync($"UPDATE events SET Sequence = 0 WHERE Id = {victim.Id}");
                break;
            default:
                await ExecuteAsync($"UPDATE events SET Sequence = -7 WHERE Id = {victim.Id}");
                break;
        }

        await using var dbContext = _fixture.CreateContext();
        var handler = new GetRunEventHistoryQueryHandler(dbContext);

        // The defective row is the oldest: it is the admitted last row of a full read and the extra row of a read one short of it.
        var full = await handler.HandleAsync(new GetRunEventHistoryQuery(runId, null, 5), CancellationToken.None);
        var extra = await handler.HandleAsync(new GetRunEventHistoryQuery(runId, null, 4), CancellationToken.None);
        var clear = await handler.HandleAsync(new GetRunEventHistoryQuery(runId, null, 3), CancellationToken.None);

        foreach (var failed in new[] { full, extra })
        {
            Assert.True(failed.IsFailure);
            var error = Assert.Single(failed.Errors);
            Assert.Equal("run_event_history.unavailable", error.Code);
            Assert.Equal(Devalente.Shared.Results.ErrorType.Conflict, error.Type);
        }

        // A page that does not select the defective header is unaffected.
        Assert.True(clear.IsSuccess);
        Assert.Equal(3, clear.Value.Entries.Count);
    }

    [Fact]
    public async Task Malformed_payloads_and_secret_sentinels_are_never_selected_or_returned()
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 3, payload: "{\"token\":\"SECRET-SENTINEL-77aa\",");
        await ExecuteAsync($"UPDATE events SET PayloadJson = 'not json at all SECRET-SENTINEL-77aa' WHERE Id = {events[1].Id}");
        await ExecuteAsync($"UPDATE events SET PayloadJson = '' WHERE Id = {events[2].Id}");

        var page = await ReadAsync(runId);

        Assert.Equal(3, page.Entries.Count);
        Assert.DoesNotContain("SECRET-SENTINEL", JsonSerializer.Serialize(page), StringComparison.Ordinal);
    }
}
