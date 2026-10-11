using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0035: the collaboration history is one read-only mediator query over minimal untracked projections of the run's own
/// messages. It pages by the existing (RunId, Sequence) index with an exclusive cursor, classifies stored enums and bounds the two text
/// columns in the database, admits content only through the existing Domain content policy, and answers through API-owned transport
/// types. The existing timeline, event and live readers are not its consumers.</summary>
public sealed partial class CollaborationHistoryBoundaryTests
{
    private const string QueryFolder = "DevalCopilot.Application/Features/Runs/Queries/GetCollaborationHistory";
    private const string Endpoint = "DevalCopilot.Api/Features/Runs/GetCollaborationHistory/GetCollaborationHistoryEndpoint.cs";
    private const string TransportNamespace = "DevalCopilot.Api.Features.Runs.GetCollaborationHistory";

    private static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevalCopilot.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "backend");
    }

    private static string Code(string path) => string.Join(
        '\n', File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
            && !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

    private static string CodeOf(string relative) => Code(Path.Combine(BackendRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string[] QuerySources()
    {
        var folder = Path.Combine(BackendRoot(), QueryFolder.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(folder), "The collaboration-history query slice must live in its own operation folder.");
        return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
    }

    private static string QueryCode() => string.Join('\n', QuerySources().Select(Code));

    [GeneratedRegex(@"dbContext\.([A-Za-z]+)")]
    private static partial Regex DbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.AsNoTracking\(\)")]
    private static partial Regex UntrackedDbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.(Add|AddRange|Remove|RemoveRange|Update|UpdateRange|Attach)\(")]
    private static partial Regex DbSetWrite();

    [Fact]
    public void The_query_only_reads_untracked_rows_of_runs_and_collaboration_messages_and_reaches_nothing_external()
    {
        var code = QueryCode();

        Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
        Assert.DoesNotMatch(DbSetWrite(), code);
        Assert.Equal(
            ["CollaborationMessages", "Runs"], DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in new[]
        {
            "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "BeginTransaction", "IsModified", "AsTracking", "TimeProvider",
            "IRunEventNotifier", "RunEvent.Record", "IArtifactStore", "IGit", "ILocalCommit", "IProcess", "ProcessStart", "IAgent", "File.", "Directory.",
            "Path.", "HttpClient", "Task.Delay", "Include(", "ThenInclude(", "FromSql", "ExecuteSql",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Paging_orders_by_descending_sequence_with_an_exclusive_cursor_and_one_extra_row_and_nothing_else()
    {
        var code = QueryCode();

        Assert.Single(Regex.Matches(code, @"\bOrderByDescending\("));
        Assert.Contains("OrderByDescending(message => message.Sequence)", code, StringComparison.Ordinal);
        Assert.Contains("message.Sequence < before", code, StringComparison.Ordinal);
        Assert.Contains("message.RunId == query.RunId", code, StringComparison.Ordinal);
        // The database page is limit + 1 rows; the only other Take trims the extra row from the already bounded in-memory page.
        Assert.Single(Regex.Matches(code, @"\.Take\(limit \+ 1\)"));
        Assert.Single(Regex.Matches(code, @"\.Take\(limit\)"));
        Assert.Equal(2, Regex.Matches(code, @"\.Take\(").Count);
        Assert.Contains("long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture)", code, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "Skip(", "SkipWhile(", "OrderBy(", "ThenBy", "CountAsync", "LongCountAsync", "Math.Min", "Math.Max", "Math.Clamp", "MaxAsync",
            "MinAsync", "OccurredAtUtc >", "OccurredAtUtc <", "FirstOrDefault(message", "LastOrDefault", "double", "float", "decimal",
            "Convert.ToDouble", "JsonSerializer",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Headers_are_minimal_projections_that_never_select_a_payload_an_attempt_a_stored_enum_or_the_entity()
    {
        var code = QueryCode();

        Assert.Contains(".Select(", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"message\.(Actor|Recipient)\b", code);
        foreach (var forbidden in new[]
        {
            "AttemptId", "InReplyToMessageId", "message.ActorKind,", "message.ActorAgentRole,", "message.ActorAgentProvider,",
            "message.RecipientKind,", "message.Type,", "message.Provenance,", "ParticipantIdentity", "FromParts", "Enum.Parse",
            "Enum.TryParse", "Enum.IsDefined", "ToListAsync<CollaborationMessage>", "Artifacts", "Attempts", "Projects",
            "LocalCommitOperations", "GitCheckpoints", "Events",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        // The two text columns are selected only under the Domain bounds, and the unavailable refusal is the single fixed conflict.
        Assert.Contains("((byte[])(object)message.Summary).Length <= summaryByteBound ? message.Summary : null", code, StringComparison.Ordinal);
        Assert.Contains(
            "((byte[])(object)message.StructuredContentJson).Length <= contentByteBound ? message.StructuredContentJson : null", code, StringComparison.Ordinal);
        Assert.Contains("const int summaryByteBound = CollaborationMessageContentPolicy.MaximumSummaryLength * 3;", code, StringComparison.Ordinal);
        Assert.Contains("const int contentByteBound = CollaborationMessageContentPolicy.MaximumStructuredContentLength * 3;", code, StringComparison.Ordinal);
        Assert.Contains("collaboration_history.unavailable", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"Error\.Conflict\("));
        Assert.Single(Regex.Matches(code, @"Error\.NotFound\("));
    }

    [Fact]
    public void Content_is_admitted_only_through_the_existing_domain_policy_and_only_its_expected_failure_is_handled()
    {
        var code = QueryCode();

        Assert.Single(Regex.Matches(code, @"CollaborationMessageContentPolicy\.Validate\("));
        Assert.Contains("CollaborationMessageContentPolicy.IsSafeSummary(summary)", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"catch \(ArgumentException\)"));
        Assert.DoesNotContain("catch (Exception", code, StringComparison.Ordinal);
        Assert.DoesNotContain("catch\n", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"JsonDocument\.Parse\("));
        Assert.DoesNotContain("JsonSerializer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Substring(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Truncate", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_endpoint_is_one_protected_bodyless_get_that_only_sends_the_query_through_the_mediator()
    {
        var root = BackendRoot();
        var code = CodeOf(Endpoint);

        Assert.Contains("[HttpGet(\"{runId:guid}/collaboration-history\")]", code, StringComparison.Ordinal);
        Assert.Contains(": RunsBaseEndpoint", code, StringComparison.Ordinal);
        Assert.Contains("IApplicationMediator mediator", code, StringComparison.Ordinal);
        Assert.Contains("mediator.SendAsync(", code, StringComparison.Ordinal);
        Assert.Contains("new GetCollaborationHistoryQuery(runId, beforeSequence, limit)", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"\[Http[A-Za-z]+\("));
        foreach (var status in new[] { "Status400BadRequest", "Status404NotFound", "Status409Conflict" })
        {
            Assert.Contains($"[ProducesResponseType<ApiProblemDetails>(StatusCodes.{status})]", code, StringComparison.Ordinal);
        }

        Assert.Contains("[FromQuery, ModelBinder(typeof(CollaborationHistoryCursorModelBinder))] string? beforeSequence", code, StringComparison.Ordinal);
        Assert.Contains("[FromQuery, ModelBinder(typeof(CollaborationHistoryLimitModelBinder))] int? limit", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromBody]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnonymous", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PayloadJson", code, StringComparison.Ordinal);
        Assert.Contains("[Authorize]", CodeOf("DevalCopilot.Api/Features/Runs/RunsBaseEndpoint.cs"), StringComparison.Ordinal);

        var routes = Directory.EnumerateFiles(Path.Combine(root, "DevalCopilot.Api"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && Code(path).Contains("collaboration-history\"", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToArray();
        Assert.Equal([Endpoint], routes);
    }

    [Fact]
    public void The_transport_types_are_api_owned_and_expose_only_primitives_and_their_own_records()
    {
        var api = SolutionAssemblies.Load(SolutionAssemblies.Api);
        var responses = api.GetTypes()
            .Where(type => type.Namespace == TransportNamespace && type.Name.EndsWith("Response", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(
            ["CollaborationHistoryEntryResponse", "CollaborationHistoryFieldResponse", "CollaborationHistoryParticipantResponse", "GetCollaborationHistoryResponse"],
            responses.Select(type => type.Name).Order(StringComparer.Ordinal));

        foreach (var response in responses)
        {
            foreach (var property in response.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
                {
                    type = type.GetGenericArguments()[0];
                }

                Assert.True(
                    type == typeof(string) || type == typeof(bool) || type == typeof(Guid) || type == typeof(DateTimeOffset)
                        || type.Namespace == TransportNamespace,
                    $"{response.Name}.{property.Name} exposes {type.FullName}");
                Assert.NotEqual(typeof(long), type);
                Assert.NotEqual(typeof(int), type);
            }
        }
    }

    [Fact]
    public void The_existing_timeline_event_and_live_readers_do_not_consume_the_history_types()
    {
        var root = BackendRoot();
        var consumers = new[]
        {
            "DevalCopilot.Application/Features/Runs/Queries/GetRunEvents/GetRunEventsQueryHandler.cs",
            "DevalCopilot.Application/Features/Runs/Queries/GetCollaborationTimeline/GetCollaborationTimelineQueryHandler.cs",
            "DevalCopilot.Api/Features/Runs/GetCollaborationTimeline/GetCollaborationTimelineEndpoint.cs",
            "DevalCopilot.Application/Features/Runs/Queries/GetRunEventHistory/GetRunEventHistoryQueryHandler.cs",
            "DevalCopilot.Api/Features/Runs/GetRunEvents/GetRunEventsEndpoint.cs",
            "DevalCopilot.Api/RealTime/SignalRRunEventNotifier.cs",
        };

        foreach (var consumer in consumers)
        {
            Assert.True(File.Exists(Path.Combine(root, consumer.Replace('/', Path.DirectorySeparatorChar))), consumer);
            Assert.DoesNotContain("CollaborationHistory", CodeOf(consumer), StringComparison.Ordinal);
        }
    }
}
