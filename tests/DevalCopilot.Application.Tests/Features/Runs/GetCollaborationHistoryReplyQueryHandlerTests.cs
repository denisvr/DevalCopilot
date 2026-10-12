using System.Data.Common;
using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryReply;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0036 at the query boundary, over real file-backed SQLite with the production migrations: the source's own stored reference is
/// resolved to at most one parent of the same run, strictly earlier, version 1.0 and of a type the reply policy allows; every other
/// stored shape is the fixed unavailable relationship without a target; the parent keeps the ADR-0035 disclosure; and the read is
/// three bounded, indexed selects that write nothing.
/// </summary>
public sealed class GetCollaborationHistoryReplyQueryHandlerTests : IAsyncLifetime
{
    private const string Secret = "SECRET-SENTINEL-77ac";

    private static readonly CollaborationMessageType[] AllTypes = Enum.GetValues<CollaborationMessageType>();

    // The ADR-0004 reply table restated independently of the Domain policy under test.
    private static readonly IReadOnlyDictionary<CollaborationMessageType, CollaborationMessageType[]> Allowed =
        new Dictionary<CollaborationMessageType, CollaborationMessageType[]>
        {
            [CollaborationMessageType.Proposal] = [CollaborationMessageType.Proposal],
            [CollaborationMessageType.Acceptance] = [CollaborationMessageType.Proposal],
            [CollaborationMessageType.Challenge] = [CollaborationMessageType.Proposal],
            [CollaborationMessageType.Decision] = [CollaborationMessageType.Proposal, CollaborationMessageType.Challenge],
            [CollaborationMessageType.ExecutionReport] = [CollaborationMessageType.Decision, CollaborationMessageType.Proposal],
            [CollaborationMessageType.ReviewFinding] = [CollaborationMessageType.ExecutionReport],
            [CollaborationMessageType.ReviewApproval] = [CollaborationMessageType.ExecutionReport],
            [CollaborationMessageType.RevisionResponse] = [CollaborationMessageType.ReviewFinding],
            [CollaborationMessageType.Question] =
            [
                CollaborationMessageType.Proposal, CollaborationMessageType.Challenge, CollaborationMessageType.Decision,
                CollaborationMessageType.ExecutionReport, CollaborationMessageType.ReviewFinding,
            ],
            [CollaborationMessageType.Escalation] =
            [
                CollaborationMessageType.Proposal, CollaborationMessageType.Challenge, CollaborationMessageType.Decision,
                CollaborationMessageType.ExecutionReport, CollaborationMessageType.ReviewFinding,
                CollaborationMessageType.RevisionResponse, CollaborationMessageType.Question,
            ],
            [CollaborationMessageType.HumanInstruction] = [CollaborationMessageType.Escalation],
        };

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

    private sealed class FaultingInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"Sequence\" <", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("injected database fault");
            }

            return ValueTask.FromResult(result);
        }
    }

    private async Task<Guid> SeedRunAsync()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Reply", $@"C:\repos\{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        dbContext.Projects.Add(project);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", DateTimeOffset.UtcNow);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    private async Task<CollaborationMessage> RecordAsync(
        Guid runId, CollaborationMessageType type, Guid? replyTo, int index = 0, string? summary = null)
    {
        var human = type == CollaborationMessageType.HumanInstruction;
        var message = CollaborationMessage.Record(
            Guid.NewGuid(),
            runId,
            null,
            CollaborationMessage.ProtocolVersionOne,
            human ? ParticipantIdentity.ForHuman() : ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForOrchestrator(),
            type,
            replyTo,
            summary ?? $"Recorded {type} summary {index}.",
            CollaborationHistoryFixtureContent.Json(type, index),
            human ? CollaborationMessageProvenance.HumanSubmitted : CollaborationMessageProvenance.ProviderObserved,
            new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero).AddSeconds(index));
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

    private async Task<Result<GetCollaborationHistoryReplyQueryResult>> SendAsync(Guid runId, Guid messageId)
    {
        await using var dbContext = _fixture.CreateContext();
        return await new GetCollaborationHistoryReplyQueryHandler(dbContext).HandleAsync(
            new GetCollaborationHistoryReplyQuery(runId, messageId), CancellationToken.None);
    }

    private async Task<GetCollaborationHistoryReplyQueryResult> ReadAsync(Guid runId, Guid messageId)
    {
        var result = await SendAsync(runId, messageId);
        Assert.True(result.IsSuccess, string.Join(", ", result.IsSuccess ? [] : result.Errors.Select(error => error.Code)));
        return result.Value;
    }

    private static void AssertUnavailable(GetCollaborationHistoryReplyQueryResult result)
    {
        Assert.Equal(CollaborationReplyStatus.Unavailable, result.Status);
        Assert.Null(result.Parent);
    }

    [Fact]
    public async Task An_unknown_run_and_a_missing_or_foreign_source_are_one_fixed_not_found()
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        var foreign = await RecordAsync(otherRun, CollaborationMessageType.Proposal, null);

        foreach (var result in new[]
        {
            await SendAsync(Guid.NewGuid(), foreign.Id),
            await SendAsync(runId, Guid.NewGuid()),
            await SendAsync(runId, foreign.Id),
            await SendAsync(runId, Guid.Empty),
        })
        {
            Assert.True(result.IsFailure);
            var error = Assert.Single(result.Errors);
            Assert.Equal(GetCollaborationHistoryReplyErrors.NotFoundCode, error.Code);
            Assert.Equal(ErrorType.NotFound, error.Type);
        }

        Assert.Equal(CollaborationReplyStatus.None, (await ReadAsync(otherRun, foreign.Id)).Status);
    }

    [Fact]
    public async Task A_root_proposal_without_a_reply_reference_is_none()
    {
        var runId = await SeedRunAsync();
        var root = await RecordAsync(runId, CollaborationMessageType.Proposal, null);

        var result = await ReadAsync(runId, root.Id);

        Assert.Equal(runId, result.RunId);
        Assert.Equal(root.Id, result.MessageId);
        Assert.Equal(root.Sequence, result.SourceSequence);
        Assert.Equal(CollaborationReplyStatus.None, result.Status);
        Assert.Null(result.Parent);
    }

    [Theory]
    [InlineData(CollaborationMessageType.Acceptance)]
    [InlineData(CollaborationMessageType.Challenge)]
    [InlineData(CollaborationMessageType.Question)]
    [InlineData(CollaborationMessageType.Decision)]
    [InlineData(CollaborationMessageType.ExecutionReport)]
    [InlineData(CollaborationMessageType.ReviewFinding)]
    [InlineData(CollaborationMessageType.RevisionResponse)]
    [InlineData(CollaborationMessageType.Escalation)]
    [InlineData(CollaborationMessageType.ReviewApproval)]
    [InlineData(CollaborationMessageType.HumanInstruction)]
    public async Task A_message_that_requires_a_reply_but_stores_none_is_unavailable_not_none(CollaborationMessageType type)
    {
        var runId = await SeedRunAsync();
        var source = await RecordAsync(runId, type, Guid.NewGuid());
        await ExecuteAsync($"UPDATE collaboration_messages SET InReplyToMessageId = NULL WHERE Id = {source.Id}");

        AssertUnavailable(await ReadAsync(runId, source.Id));
    }

    public static TheoryData<CollaborationMessageType, CollaborationMessageType> TypeCombinations()
    {
        var data = new TheoryData<CollaborationMessageType, CollaborationMessageType>();
        foreach (var source in AllTypes)
        {
            foreach (var parent in AllTypes)
            {
                data.Add(source, parent);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TypeCombinations))]
    public async Task Every_source_and_parent_type_pair_follows_the_reply_policy(
        CollaborationMessageType sourceType, CollaborationMessageType parentType)
    {
        var runId = await SeedRunAsync();
        var parent = await RecordAsync(runId, parentType, parentType == CollaborationMessageType.Proposal ? null : Guid.NewGuid(), 3);
        var source = await RecordAsync(runId, sourceType, parent.Id, 4);

        var result = await ReadAsync(runId, source.Id);

        if (Allowed[sourceType].Contains(parentType))
        {
            Assert.Equal(CollaborationReplyStatus.Available, result.Status);
            Assert.Equal(parent.Id, result.Parent!.Id);
            Assert.Equal(parentType.ToString(), result.Parent.Type);
            Assert.Equal(parent.Sequence, result.Parent.Sequence);
        }
        else
        {
            AssertUnavailable(result);
        }
    }

    [Fact]
    public async Task The_exact_parent_is_resolved_beyond_any_loaded_page_and_equals_its_history_entry()
    {
        var runId = await SeedRunAsync();
        var root = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        for (var index = 1; index <= 30; index++)
        {
            await RecordAsync(runId, CollaborationMessageType.Proposal, root.Id, index);
        }

        var source = await RecordAsync(runId, CollaborationMessageType.Challenge, root.Id, 31);

        var result = await ReadAsync(runId, source.Id);

        Assert.Equal(CollaborationReplyStatus.Available, result.Status);
        await using var dbContext = _fixture.CreateContext();
        var history = new GetCollaborationHistoryQueryHandler(dbContext);
        var cursor = (string?)null;
        CollaborationHistoryEntry? fromHistory = null;
        while (fromHistory is null)
        {
            var page = (await history.HandleAsync(new GetCollaborationHistoryQuery(runId, cursor, 20), CancellationToken.None)).Value;
            fromHistory = page.Entries.SingleOrDefault(entry => entry.Id == root.Id);
            cursor = page.NextBeforeSequence?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(fromHistory is not null || cursor is not null);
        }

        Assert.Equal(fromHistory with { Fields = [] }, result.Parent! with { Fields = [] });
        Assert.Equal(fromHistory.Fields, result.Parent!.Fields);
    }

    [Fact]
    public async Task Only_the_one_stored_parent_is_resolved_never_a_chain_or_a_neighbor()
    {
        var runId = await SeedRunAsync();
        var grandparent = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        var latestProposal = await RecordAsync(runId, CollaborationMessageType.Proposal, grandparent.Id, 1);
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, grandparent.Id, 2);
        var newerProposal = await RecordAsync(runId, CollaborationMessageType.Proposal, grandparent.Id, 3);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, parent.Id, 4);

        var result = await ReadAsync(runId, source.Id);

        Assert.Equal(parent.Id, result.Parent!.Id);
        Assert.NotEqual(latestProposal.Id, result.Parent.Id);
        Assert.NotEqual(newerProposal.Id, result.Parent.Id);

        // A parent whose own reference is broken is still this source's valid parent: nothing is traversed.
        await ExecuteAsync($"UPDATE collaboration_messages SET InReplyToMessageId = {Guid.NewGuid()} WHERE Id = {parent.Id}");
        Assert.Equal(CollaborationReplyStatus.Available, (await ReadAsync(runId, source.Id)).Status);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("self")]
    [InlineData("missing")]
    [InlineData("foreign-run")]
    [InlineData("forward")]
    [InlineData("parent-version")]
    [InlineData("parent-unknown-type")]
    [InlineData("source-version")]
    [InlineData("source-unknown-type")]
    public async Task A_broken_stored_reference_is_unavailable_with_no_target(string defect)
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        var foreign = await RecordAsync(otherRun, CollaborationMessageType.Proposal, null, 0);
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 1);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, parent.Id, 2);
        var later = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 3);
        Assert.Equal(CollaborationReplyStatus.Available, (await ReadAsync(runId, source.Id)).Status);

        switch (defect)
        {
            case "empty":
                await ExecuteAsync($"UPDATE collaboration_messages SET InReplyToMessageId = '00000000-0000-0000-0000-000000000000' WHERE Id = {source.Id}");
                break;
            case "self":
                await ExecuteAsync($"UPDATE collaboration_messages SET InReplyToMessageId = Id WHERE Id = {source.Id}");
                break;
            case "missing":
                await ExecuteAsync($"UPDATE collaboration_messages SET InReplyToMessageId = {Guid.NewGuid()} WHERE Id = {source.Id}");
                break;
            case "foreign-run":
                await ExecuteAsync($"UPDATE collaboration_messages SET InReplyToMessageId = {foreign.Id} WHERE Id = {source.Id}");
                break;
            case "forward":
                await ExecuteAsync($"UPDATE collaboration_messages SET InReplyToMessageId = {later.Id} WHERE Id = {source.Id}");
                break;
            case "parent-version":
                await ExecuteAsync($"UPDATE collaboration_messages SET ProtocolVersion = '2.0' WHERE Id = {parent.Id}");
                break;
            case "parent-unknown-type":
                await ExecuteAsync($"UPDATE collaboration_messages SET Type = {"Overlord" + Secret} WHERE Id = {parent.Id}");
                break;
            case "source-version":
                await ExecuteAsync($"UPDATE collaboration_messages SET ProtocolVersion = '2.0' WHERE Id = {source.Id}");
                break;
            default:
                await ExecuteAsync($"UPDATE collaboration_messages SET Type = {"Overlord" + Secret} WHERE Id = {source.Id}");
                break;
        }

        var result = await ReadAsync(runId, source.Id);

        AssertUnavailable(result);
        Assert.Equal(source.Id, result.MessageId);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_source_type_or_version_is_unavailable_even_without_a_reference()
    {
        var runId = await SeedRunAsync();
        var root = await RecordAsync(runId, CollaborationMessageType.Proposal, null);
        await ExecuteAsync($"UPDATE collaboration_messages SET ProtocolVersion = '2.0' WHERE Id = {root.Id}");
        AssertUnavailable(await ReadAsync(runId, root.Id));

        await ExecuteAsync($"UPDATE collaboration_messages SET ProtocolVersion = '1.0', Type = 'proposal' WHERE Id = {root.Id}");
        AssertUnavailable(await ReadAsync(runId, root.Id));
    }

    [Fact]
    public async Task Sequences_are_compared_and_returned_exactly_across_gaps_two_to_the_fifty_third_and_int64()
    {
        var runId = await SeedRunAsync();
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, parent.Id, 1);

        long[][] cases =
        [
            [9007199254740992, 9007199254740993],
            [9007199254740993, 9007199254740994],
            [9223372036854775806, long.MaxValue],
            [41, 9007199254740993],
            [1, 2],
        ];
        foreach (var pair in cases)
        {
            await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = {pair[1]} WHERE Id = {source.Id}");
            await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = {pair[0]} WHERE Id = {parent.Id}");

            var result = await ReadAsync(runId, source.Id);

            Assert.Equal(pair[1], result.SourceSequence);
            Assert.Equal(CollaborationReplyStatus.Available, result.Status);
            Assert.Equal(pair[0], result.Parent!.Sequence);
        }

        // Forward by exactly one above 2^53, where a floating-point comparison cannot tell them apart.
        await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = 9007199254740992 WHERE Id = {source.Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = 9007199254740993 WHERE Id = {parent.Id}");
        AssertUnavailable(await ReadAsync(runId, source.Id));
    }

    [Theory]
    [InlineData("zero-sequence")]
    [InlineData("negative-sequence")]
    [InlineData("empty-identity")]
    public async Task An_unusable_stored_source_identity_or_sequence_is_one_fixed_conflict(string defect)
    {
        var runId = await SeedRunAsync();
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, parent.Id, 1);
        var messageId = source.Id;
        switch (defect)
        {
            case "zero-sequence":
                await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = 0 WHERE Id = {source.Id}");
                break;
            case "negative-sequence":
                await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = -7 WHERE Id = {source.Id}");
                break;
            default:
                await ExecuteAsync($"UPDATE collaboration_messages SET Id = '00000000-0000-0000-0000-000000000000' WHERE Id = {source.Id}");
                messageId = Guid.Empty;
                break;
        }

        var result = await SendAsync(runId, messageId);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("collaboration_reply.unavailable", error.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.DoesNotContain(Secret, error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_parent_with_a_nonpositive_sequence_is_an_unavailable_relationship_not_a_failure()
    {
        var runId = await SeedRunAsync();
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, parent.Id, 1);
        await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = -4 WHERE Id = {parent.Id}");

        AssertUnavailable(await ReadAsync(runId, source.Id));
    }

    [Theory]
    [InlineData("Provenance", "Forged")]
    [InlineData("ActorKind", "Wizard")]
    [InlineData("ActorAgentRole", "Overlord")]
    [InlineData("RecipientAgentProvider", "Gemini")]
    public async Task A_parent_with_unknown_recorded_metadata_keeps_the_relationship_but_not_its_content(string column, string stored)
    {
        var runId = await SeedRunAsync();
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, parent.Id, 1);
        var value = stored + Secret;
        await ExecuteAsync(column switch
        {
            "Provenance" => $"UPDATE collaboration_messages SET Provenance = {value} WHERE Id = {parent.Id}",
            "ActorKind" => $"UPDATE collaboration_messages SET ActorKind = {value} WHERE Id = {parent.Id}",
            "ActorAgentRole" => $"UPDATE collaboration_messages SET ActorAgentRole = {value} WHERE Id = {parent.Id}",
            _ => $"UPDATE collaboration_messages SET RecipientAgentProvider = {value} WHERE Id = {parent.Id}",
        });

        var result = await ReadAsync(runId, source.Id);

        Assert.Equal(CollaborationReplyStatus.Available, result.Status);
        Assert.Equal(parent.Id, result.Parent!.Id);
        Assert.Equal("Proposal", result.Parent.Type);
        Assert.False(result.Parent.ContentAvailable);
        Assert.Null(result.Parent.Summary);
        Assert.Empty(result.Parent.Fields);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    public static TheoryData<string, string> DefectiveContent() => new()
    {
        { "summary", "" },
        { "summary", "The password is hunter2 " + Secret },
        { "summary", new string('s', 601) },
        { "json", "not json " + Secret },
        { "json", "{\"scope\":\"" + Secret + "\"}" },
        { "json", "{\"scope\":\"a\",\"implementationSteps\":\"b\",\"risks\":\"c\",\"verificationPlan\":\"d\",\"escalationPoints\":\"e\",\"extra\":\"" + Secret + "\"}" },
        { "json", "{\"scope\":\"" + new string('x', 5000) + "\"}" },
        { "json", "{}" },
    };

    [Theory]
    [MemberData(nameof(DefectiveContent))]
    public async Task A_parent_with_unsafe_malformed_or_oversized_content_is_available_with_a_header_only_entry(string column, string stored)
    {
        var runId = await SeedRunAsync();
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, parent.Id, 1);
        if (column == "summary")
        {
            await ExecuteAsync($"UPDATE collaboration_messages SET Summary = {stored} WHERE Id = {parent.Id}");
        }
        else
        {
            await ExecuteAsync($"UPDATE collaboration_messages SET StructuredContentJson = {stored} WHERE Id = {parent.Id}");
        }

        var result = await ReadAsync(runId, source.Id);

        Assert.Equal(CollaborationReplyStatus.Available, result.Status);
        var entry = result.Parent!;
        Assert.Equal(parent.Id, entry.Id);
        Assert.Equal(parent.Sequence, entry.Sequence);
        Assert.Equal("Proposal", entry.Type);
        Assert.Equal("1.0", entry.ProtocolVersion);
        Assert.Equal("ProviderObserved", entry.Provenance);
        Assert.False(entry.ContentAvailable);
        Assert.Null(entry.Summary);
        Assert.Empty(entry.Fields);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(Secret, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_healthy_parent_discloses_its_summary_and_every_field()
    {
        var runId = await SeedRunAsync();
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 7);
        var source = await RecordAsync(runId, CollaborationMessageType.Challenge, parent.Id, 8);

        var entry = (await ReadAsync(runId, source.Id)).Parent!;

        Assert.True(entry.ContentAvailable);
        Assert.Equal("Recorded Proposal summary 7.", entry.Summary);
        Assert.Equal(
            CollaborationHistoryFixtureContent.FieldNames[CollaborationMessageType.Proposal]
                .Select(name => new CollaborationHistoryField(name, CollaborationHistoryFixtureContent.Value(name, 7))),
            entry.Fields);
    }

    [Fact]
    public async Task An_oversized_parent_text_column_is_never_selected_and_a_nul_cannot_hide_it()
    {
        var runId = await SeedRunAsync();
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, parent.Id, 1);
        var bytes = System.Text.Encoding.UTF8.GetBytes("ok\0" + new string('x', 3_000_000));
        await using (var write = _fixture.CreateContext())
        {
            await write.Database.ExecuteSqlRawAsync(
                "UPDATE collaboration_messages SET Summary = CAST({0} AS TEXT), StructuredContentJson = CAST({0} AS TEXT) WHERE Id = {1}",
                bytes, parent.Id);
        }

        var result = await ReadAsync(runId, source.Id);

        Assert.Equal(CollaborationReplyStatus.Available, result.Status);
        Assert.False(result.Parent!.ContentAvailable);
        Assert.True(JsonSerializer.Serialize(result).Length < 5_000);
    }

    [Fact]
    public async Task The_read_is_three_indexed_selects_with_database_bounds_that_write_nothing()
    {
        var runId = await SeedRunAsync();
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, parent.Id, 1);
        var recorder = new RecordingInterceptor();
        await using var dbContext = _fixture.CreateContext(recorder);

        var result = await new GetCollaborationHistoryReplyQueryHandler(dbContext).HandleAsync(
            new GetCollaborationHistoryReplyQuery(runId, source.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, recorder.Commands.Count);
        Assert.All(recorder.Commands, command => Assert.StartsWith("SELECT", command.TrimStart(), StringComparison.OrdinalIgnoreCase));
        Assert.All(recorder.Commands, command =>
        {
            foreach (var forbidden in new[] { "OFFSET", "COUNT(", "JOIN", "AttemptId", "ArtifactId", "UPDATE", "INSERT", "DELETE" })
            {
                Assert.DoesNotContain(forbidden, command, StringComparison.OrdinalIgnoreCase);
            }
        });
        var reads = recorder.Commands.Where(command => command.Contains("FROM \"collaboration_messages\"", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, reads.Count);

        var sourceRead = reads[0];
        Assert.Contains("\"RunId\" =", sourceRead, StringComparison.Ordinal);
        Assert.Contains("\"Id\" =", sourceRead, StringComparison.Ordinal);
        Assert.Contains("InReplyToMessageId", sourceRead, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Summary\"", sourceRead, StringComparison.Ordinal);
        Assert.DoesNotContain("\"StructuredContentJson\"", sourceRead, StringComparison.Ordinal);

        var parentRead = reads[1];
        Assert.Contains("\"RunId\" =", parentRead, StringComparison.Ordinal);
        Assert.Contains("\"Id\" =", parentRead, StringComparison.Ordinal);
        Assert.Contains("\"Sequence\" <", parentRead, StringComparison.Ordinal);
        Assert.Contains("\"ProtocolVersion\" =", parentRead, StringComparison.Ordinal);
        Assert.DoesNotContain("InReplyToMessageId", parentRead, StringComparison.Ordinal);
        Assert.Matches(
            @"(?i)CASE\s+WHEN\s+length\(CAST\([^)]*""Summary""\s+AS\s+BLOB\)\)\s*<=\s*1800\s+THEN\s+[^\s,]*""Summary""\s+END", parentRead);
        Assert.Matches(
            @"(?i)CASE\s+WHEN\s+length\(CAST\([^)]*""StructuredContentJson""\s+AS\s+BLOB\)\)\s*<=\s*15000\s+THEN\s+[^\s,]*""StructuredContentJson""\s+END",
            parentRead);
    }

    [Fact]
    public async Task A_root_or_broken_reference_never_issues_a_parent_read()
    {
        var runId = await SeedRunAsync();
        var root = await RecordAsync(runId, CollaborationMessageType.Proposal, null);
        var recorder = new RecordingInterceptor();
        await using var dbContext = _fixture.CreateContext(recorder);

        await new GetCollaborationHistoryReplyQueryHandler(dbContext).HandleAsync(
            new GetCollaborationHistoryReplyQuery(runId, root.Id), CancellationToken.None);

        Assert.Equal(2, recorder.Commands.Count);
    }

    [Fact]
    public async Task A_database_fault_propagates_and_is_not_masked_as_an_unavailable_relationship()
    {
        var runId = await SeedRunAsync();
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, parent.Id, 1);
        await using var dbContext = _fixture.CreateContext(new FaultingInterceptor());

        await Assert.ThrowsAsync<InvalidOperationException>(() => new GetCollaborationHistoryReplyQueryHandler(dbContext).HandleAsync(
            new GetCollaborationHistoryReplyQuery(runId, source.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Reading_changes_no_stored_row()
    {
        var runId = await SeedRunAsync();
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, parent.Id, 1);
        async Task<string> SnapshotAsync()
        {
            await using var dbContext = _fixture.CreateContext();
            var rows = await dbContext.CollaborationMessages.AsNoTracking().OrderBy(message => message.Sequence).ToListAsync();
            var events = await dbContext.Events.AsNoTracking().CountAsync();
            return JsonSerializer.Serialize(rows.Select(row => new
            {
                row.Id,
                row.Sequence,
                row.InReplyToMessageId,
                row.Summary,
                row.StructuredContentJson,
                row.Type,
                row.ProtocolVersion,
            })) + events;
        }

        var before = await SnapshotAsync();
        await ReadAsync(runId, source.Id);
        await ReadAsync(runId, parent.Id);

        Assert.Equal(before, await SnapshotAsync());
    }
}
