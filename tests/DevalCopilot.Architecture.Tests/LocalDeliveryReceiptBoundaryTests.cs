using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0032: the receipt is one read-only mediator query over pinned persisted rows. It never reads "current" or "latest"
/// state, never writes, never touches the repository, a process or a provider, shares the recorded digest format with admission, and
/// answers through API-owned transport types that leak no Domain or Application entity.</summary>
public sealed partial class LocalDeliveryReceiptBoundaryTests
{
    private const string QueryFolder = "DevalCopilot.Application/Features/Runs/Queries/GetLocalDeliveryReceipt";
    private const string Reader = QueryFolder + "/LocalDeliveryReceiptReader.cs";
    private const string Handler = QueryFolder + "/GetLocalDeliveryReceiptQueryHandler.cs";
    private const string Digests = "DevalCopilot.Application/Features/Runs/Policies/LocalCommit/LocalCommitMemberDigests.cs";
    private const string Endpoint = "DevalCopilot.Api/Features/Runs/GetLocalDeliveryReceipt/GetLocalDeliveryReceiptEndpoint.cs";

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

    private static IEnumerable<string> ProductionSources(string root) => Directory
        .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Code(string path) => string.Join(
        '\n', File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
            && !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

    private static string CodeOf(string relative) => Code(Path.Combine(BackendRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string[] SourcesContaining(string root, string text) => ProductionSources(root)
        .Where(path => Code(path).Contains(text, StringComparison.Ordinal))
        .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
        .Order(StringComparer.Ordinal)
        .ToArray();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+")]
    private static partial Regex DbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.AsNoTracking\(\)")]
    private static partial Regex UntrackedDbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.(Add|AddRange|Remove|RemoveRange|Update|UpdateRange|Attach)\(")]
    private static partial Regex DbSetWrite();

    [Fact]
    public void The_query_and_its_reader_only_read_untracked_rows_and_write_or_reach_nothing_external()
    {
        foreach (var source in new[] { Handler, Reader })
        {
            var code = CodeOf(source);

            Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
            Assert.DoesNotMatch(DbSetWrite(), code);
            foreach (var forbidden in new[]
            {
                "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "BeginTransaction", "IsModified", "AsTracking", "TimeProvider",
                "IRunEventNotifier", "RunEvent.Record",
                "IArtifactStore", "IGit", "ILocalCommit", "Process", "IAgent", "File.", "Directory.", "Path.", "HttpClient", "Task.Delay",
            })
            {
                Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_reader_resolves_pinned_identifiers_and_never_the_latest_or_current_state()
    {
        var code = CodeOf(Reader);

        foreach (var forbidden in new[]
        {
            "OrderByDescending", "LocalCommitAuthorityReader", "ImplementerExecutionReportEligibility", "ResolveTip", "IsEnabled",
            "VerificationCommands", "WorkspaceStatus", "RepositoryMutationLeases", "LeaseStatus", "CurrentRunExecutionMode", "MaxAsync",
            "FirstOrDefault", "LastOrDefault", "ExpectedParent",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        // The run is read only to confirm that the PINNED run records the completion this operation records: by its own id, and
        // against the one terminal value, never a current-state eligibility such as Running, Created or Abandoned.
        Assert.Single(Regex.Matches(code, @"dbContext\.Runs\b"));
        Assert.Contains("candidate.Id == runId && candidate.Lifecycle == RunLifecycle.Completed", code, StringComparison.Ordinal);
        Assert.All(Regex.Matches(code, @"RunLifecycle\.(\w+)").Select(match => match.Groups[1].Value), name => Assert.Equal("Completed", name));
        Assert.All(Regex.Matches(code, @"RunStage\.(\w+)").Select(match => match.Groups[1].Value), name => Assert.Equal("Completed", name));

        foreach (var pinned in new[]
        {
            "operation.ExecutionReportMessageId", "operation.CodeReviewAttemptId", "operation.CodeReviewApprovalMessageId",
            "operation.AgentCheckpointReviewId", "operation.HumanCheckpointReviewId", "operation.GitCheckpointId",
            "operation.GitWorkspaceId", "member.SubjectId", "member.OperationId == operation.Id",
        })
        {
            Assert.Contains(pinned, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Admission_and_the_receipt_share_exactly_one_digest_serialization()
    {
        var root = BackendRoot();

        Assert.Equal(
            [
                "DevalCopilot.Application/Features/Runs/Policies/LocalCommit/LocalCommitAuthorityReader.cs",
                Reader,
            ],
            SourcesContaining(root, "LocalCommitMemberDigests.Verification("));
        Assert.Equal(
            [
                "DevalCopilot.Application/Features/Runs/Policies/LocalCommit/LocalCommitAuthorityReader.cs",
                Reader,
            ],
            SourcesContaining(root, "LocalCommitMemberDigests.HumanDecision("));
        Assert.Equal([Digests], SourcesContaining(root, "{review.Decision}|{review.ActorKind}"));
        Assert.Equal([Digests], SourcesContaining(root, "{executionNumber}|{commandName}"));
        Assert.Equal(
            ["DevalCopilot.Application/Features/Runs/Policies/LocalCommit/LocalCommitAuthorityReader.cs"],
            SourcesContaining(root, "local-commit-authority-v1"));
    }

    [Fact]
    public void The_endpoint_is_one_protected_bodyless_get_that_only_sends_the_query_through_the_mediator()
    {
        var root = BackendRoot();
        var code = CodeOf(Endpoint);

        Assert.Contains("[HttpGet(\"{runId:guid}/local-delivery-receipt\")]", code, StringComparison.Ordinal);
        Assert.Contains(": RunsBaseEndpoint", code, StringComparison.Ordinal);
        Assert.Contains("IApplicationMediator mediator", code, StringComparison.Ordinal);
        Assert.Contains("mediator.SendAsync(new GetLocalDeliveryReceiptQuery(runId)", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"\[Http[A-Za-z]+\("));
        Assert.DoesNotContain("[FromBody]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnonymous", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", code, StringComparison.Ordinal);
        Assert.Equal([Endpoint], SourcesContaining(root, "local-delivery-receipt\""));
        Assert.Contains("[Authorize]", CodeOf("DevalCopilot.Api/Features/Runs/RunsBaseEndpoint.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_transport_types_are_api_owned_and_expose_only_primitives_and_their_own_records()
    {
        var api = SolutionAssemblies.Load(SolutionAssemblies.Api);
        const string TransportNamespace = "DevalCopilot.Api.Features.Runs.GetLocalDeliveryReceipt";
        var responses = api.GetTypes()
            .Where(type => type.Namespace == TransportNamespace && type.Name.EndsWith("Response", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(
            [
                "GetLocalDeliveryReceiptResponse", "LocalDeliveryCheckpointResponse", "LocalDeliveryCodeReviewResponse",
                "LocalDeliveryHumanReviewResponse", "LocalDeliveryReceiptResponse", "LocalDeliveryVerificationResponse",
            ],
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
                    type == typeof(string) || type == typeof(int) || type == typeof(Guid) || type == typeof(DateTimeOffset)
                        || type.Namespace == TransportNamespace,
                    $"{response.Name}.{property.Name} exposes {type.FullName}");
            }
        }
    }
}
