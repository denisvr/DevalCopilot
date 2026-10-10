using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0033: the project run history is one read-only mediator query over minimal untracked projections of the project's own
/// runs and their own recorded local-commit operations. It pages by the unique project/execution-number index with an exclusive
/// cursor, never reconstructs a receipt or reads current state, and answers through API-owned transport types.</summary>
public sealed partial class ProjectRunHistoryBoundaryTests
{
    private const string QueryFolder = "DevalCopilot.Application/Features/Projects/Queries/GetProjectRunHistory";
    private const string Endpoint = "DevalCopilot.Api/Features/Projects/GetProjectRunHistory/GetProjectRunHistoryEndpoint.cs";

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
        Assert.True(Directory.Exists(folder), "The history query slice must live in its own operation folder.");
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
    public void The_query_only_reads_untracked_rows_of_runs_projects_and_their_own_operations_and_reaches_nothing_external()
    {
        var code = QueryCode();

        Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
        Assert.DoesNotMatch(DbSetWrite(), code);
        Assert.Equal(
            ["LocalCommitOperations", "Projects", "Runs"],
            DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in new[]
        {
            "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "BeginTransaction", "IsModified", "AsTracking", "TimeProvider",
            "IRunEventNotifier", "RunEvent.Record", "IArtifactStore", "IGit", "ILocalCommit", "Process", "IAgent", "File.", "Directory.",
            "Path.", "HttpClient", "Task.Delay", "Include(", "ThenInclude(",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Paging_orders_by_descending_execution_number_with_an_exclusive_cursor_and_one_extra_row_and_nothing_else()
    {
        var code = QueryCode();

        Assert.Single(Regex.Matches(code, @"\bOrderByDescending\("));
        Assert.Contains("OrderByDescending(run => run.ExecutionNumber)", code, StringComparison.Ordinal);
        Assert.Contains("run.ExecutionNumber < before", code, StringComparison.Ordinal);
        Assert.Contains("run.ProjectId == query.ProjectId", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"\.Take\("));
        Assert.Contains("Take(limit + 1)", code, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "Skip(", "SkipWhile(", "OrderBy(", "ThenBy", "CountAsync", "LongCountAsync", "Math.Min", "Math.Max", "Math.Clamp", "MaxAsync",
            "MinAsync", "CreatedAtUtc >", "CreatedAtUtc <", "LastAdvancedAtUtc >", "LastAdvancedAtUtc <", "FirstOrDefault", "LastOrDefault",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Entries_are_minimal_projections_and_the_execution_mode_uses_the_one_exact_storage_reading()
    {
        var code = QueryCode();

        Assert.Contains("EF.Property<string>(run, Run.ExecutionModeStorageProperty)", code, StringComparison.Ordinal);
        Assert.Contains("RunExecutionModeStorage.Read(", code, StringComparison.Ordinal);
        Assert.Contains(".Select(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RunExecutionMode)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ToListAsync<Run>", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_source_is_the_runs_own_operation_and_the_query_neither_reconstructs_a_receipt_nor_reads_current_state()
    {
        var code = QueryCode();

        foreach (var forbidden in new[]
        {
            "LocalDeliveryReceiptReader", "LocalCommitMemberDigests", "LocalCommitAuthorityReader", "LocalCommitAuthorityMembers",
            "CheckpointReviews", "CheckpointReviewEvidence", "VerificationExecutions", "VerificationCommands", "CollaborationMessages",
            "Attempts", "GitCheckpoints", "GitWorkspaces", "RepositoryMutationLeases", "CurrentRunExecutionMode", "ResolveTip", "IsEnabled",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        // Operations are looked up only for the page's own run identifiers and only the recorded Completed status classifies one.
        Assert.Single(Regex.Matches(code, @"dbContext\.LocalCommitOperations\b"));
        Assert.Contains("runIds.Contains(operation.RunId)", code, StringComparison.Ordinal);
        Assert.Contains("LocalCommitStatus.Completed", code, StringComparison.Ordinal);
        Assert.All(Regex.Matches(code, @"LocalCommitStatus\.(\w+)").Select(match => match.Groups[1].Value), name => Assert.Equal("Completed", name));
    }

    [Fact]
    public void The_endpoint_is_one_protected_bodyless_get_that_only_sends_the_query_through_the_mediator()
    {
        var root = BackendRoot();
        var code = CodeOf(Endpoint);

        Assert.Contains("[HttpGet(\"{projectId:guid}/run-history\")]", code, StringComparison.Ordinal);
        Assert.Contains(": ProjectsBaseEndpoint", code, StringComparison.Ordinal);
        Assert.Contains("IApplicationMediator mediator", code, StringComparison.Ordinal);
        Assert.Contains("mediator.SendAsync(", code, StringComparison.Ordinal);
        Assert.Contains("new GetProjectRunHistoryQuery(projectId, beforeExecutionNumber, limit)", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"\[Http[A-Za-z]+\("));
        Assert.Contains("[ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]", code, StringComparison.Ordinal);
        Assert.Contains("[ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]", code, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(code, @"\[FromQuery, ModelBinder\(typeof\(RunHistoryScalarModelBinder\)\)\] int\? ").Count);
        Assert.DoesNotContain("[FromBody]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnonymous", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.", code, StringComparison.Ordinal);
        Assert.Contains("[Authorize]", CodeOf("DevalCopilot.Api/Features/Projects/ProjectsBaseEndpoint.cs"), StringComparison.Ordinal);

        var routes = Directory.EnumerateFiles(Path.Combine(root, "DevalCopilot.Api"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && Code(path).Contains("run-history\"", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToArray();
        Assert.Equal([Endpoint], routes);
    }

    [Fact]
    public void The_transport_types_are_api_owned_and_expose_only_primitives_and_their_own_records()
    {
        var api = SolutionAssemblies.Load(SolutionAssemblies.Api);
        const string TransportNamespace = "DevalCopilot.Api.Features.Projects.GetProjectRunHistory";
        var responses = api.GetTypes()
            .Where(type => type.Namespace == TransportNamespace && type.Name.EndsWith("Response", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(
            ["GetProjectRunHistoryResponse", "ProjectRunHistoryEntryResponse", "ProjectRunHistoryReceiptSourceResponse"],
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
                    type == typeof(string) || type == typeof(int) || type == typeof(bool) || type == typeof(Guid)
                        || type == typeof(DateTimeOffset) || type.Namespace == TransportNamespace,
                    $"{response.Name}.{property.Name} exposes {type.FullName}");
            }
        }
    }
}
