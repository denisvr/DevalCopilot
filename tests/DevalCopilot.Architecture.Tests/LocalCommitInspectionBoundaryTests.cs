using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0070: the recorded local-commit inspection is one read-only mediator query over one bounded, untracked scalar statement.
/// It never invokes the live local-commit status, authority, facts, recovery or recorder policies, the Git ports or the artifact store,
/// reads no authority member, digest, message, author, physical receipt, path or latest row, projects the prepared commit identity only
/// for a Completed operation, and swallows no fault. The endpoint is an authorized bodyless MVC GET that dispatches through the mediator
/// and answers with an API-owned graph.</summary>
public sealed partial class LocalCommitInspectionBoundaryTests
{
    private static readonly string[] ApplicationFolders =
    [
        "DevalCopilot.Application/Features/Runs/Queries/GetLocalCommitInspection",
        "DevalCopilot.Application/Features/Runs/Policies/RecordedLocalCommit",
    ];

    private static readonly string[] ApiFolders = ["DevalCopilot.Api/Features/Runs/GetLocalCommitInspection"];

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

    private static string[] Sources(IEnumerable<string> folders) => folders
        .SelectMany(folder =>
        {
            var path = Path.Combine(BackendRoot(), folder.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(path), $"{folder} must exist as its own operation folder.");
            return Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories);
        })
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string ApplicationCode() => string.Join('\n', Sources(ApplicationFolders).Select(Code));

    private static string ApiCode() => string.Join('\n', Sources(ApiFolders).Select(Code));

    private static string ReaderCode() => Code(Path.Combine(
        BackendRoot(), "DevalCopilot.Application", "Features", "Runs", "Policies", "RecordedLocalCommit", "RecordedLocalCommitReader.cs"));

    [GeneratedRegex(@"dbContext\.([A-Za-z]+)")]
    private static partial Regex DbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.AsNoTracking\(\)")]
    private static partial Regex UntrackedDbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.(Add|AddRange|Remove|RemoveRange|Update|UpdateRange|Attach)\(")]
    private static partial Regex DbSetWrite();

    [Fact]
    public void The_application_slice_only_reads_untracked_rows_of_the_five_recorded_tables_and_writes_nothing()
    {
        var code = ApplicationCode();

        Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
        Assert.DoesNotMatch(DbSetWrite(), code);
        Assert.Equal(
            ["GitCheckpoints", "GitWorkspaces", "LocalCommitOperations", "Projects", "Runs"],
            DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in new[]
        {
            "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "BeginTransaction", "IsModified", "AsTracking", "TimeProvider",
            "IRunEventNotifier", "RunEvent.Record", "IApplicationMediator", "IGit", "ILocalCommit", "IProcess", "ProcessStart", "IAgent",
            "IArtifactStore", "File.", "Directory.", "Path.Combine", "Path.GetFull", "HttpClient", "Task.Delay", "Include(", "ThenInclude(",
            "FromSql", "ExecuteSql", "Environment.", "Process.",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_live_status_authority_facts_recovery_recorder_or_lease_policy_is_consulted_and_nothing_reads_a_latest_row()
    {
        var code = ApplicationCode();

        foreach (var forbidden in new[]
        {
            "LocalCommitAuthorityReader", "LocalCommitFactsReader", "LocalCommitRecoveryPolicy", "LocalCommitOutcomeRecorder",
            "LocalCommitMemberDigests", "LocalCommitHeadChain", "LocalCommitOperationView", "GetLocalCommitStatusQuery",
            "GetEligibleLocalCommitOperations", "LocalCommitAuthorityMembers", "RepositoryMutationLeases", "WorkspaceExecution",
            "ExecutionCapacity", "LocalExecution", "Commands.", "CommandHandler", "OrderByDescending", "MaxAsync", "MinAsync",
            "LastOrDefault", "Latest", "Current", "CheckpointReviews", "Attempts", "CollaborationMessages", "Events",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_fault_is_swallowed_and_every_column_beyond_the_recorded_facts_is_never_selected()
    {
        var code = ApplicationCode();

        Assert.DoesNotContain("catch", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ToListAsync", code, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "RequestSha256", "AuthoritySha256", "NormalizedMessage", "AuthorName", "AuthorEmail", "CommitTimeUnixSeconds",
            "IndexPreimageSha256", "PreparedIndexSha256", "PreparedIndexRelativePath", "IndexAdministrativeDirectoryIdentity",
            "IndexPreimageIdentity", "PreparedIndexArtifactIdentity", "IndexLockIdentity", "IndexAcquiredAtUtc", "IndexQuarantineName",
            "IndexReplacementPlannedAtUtc", "RepositoryMutationLeaseId", "CodeReviewAttemptId", "CodeReviewApprovalMessageId",
            "ExecutionReportMessageId", "AgentCheckpointReviewId", "HumanCheckpointReviewId", "WorkspacePath", "SourceCommitSha",
            "SourceBranchName", "LastFailureReasonCode", "Members",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        // Every identifier and time is projected as bounded raw bytes and admitted by the one storage parser: no row record can hold a
        // typed Guid or DateTimeOffset, so no projection can materialize one.
        foreach (var rowName in Directory.EnumerateFiles(
            Path.Combine(BackendRoot(), "DevalCopilot.Application", "Features", "Runs", "Policies", "RecordedLocalCommit"), "*Row.cs"))
        {
            var row = Code(rowName);
            Assert.DoesNotContain("Guid ", row, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTimeOffset", row, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void One_bounded_statement_proves_the_run_the_association_and_the_named_parents_and_the_commit_is_only_projected_when_completed()
    {
        var reader = ReaderCode();
        var projection = Code(Path.Combine(
            BackendRoot(), "DevalCopilot.Application", "Features", "Runs", "Policies", "RecordedLocalCommit",
            "RecordedLocalCommitProjection.cs"));

        Assert.Single(Regex.Matches(reader, @"ToArrayAsync\("));
        Assert.Single(Regex.Matches(reader, @"\.Take\(2\)"));
        Assert.Equal(3, Regex.Matches(reader, @"\.DefaultIfEmpty\(\)").Count);
        Assert.Contains("join project in dbContext.Projects.AsNoTracking() on run.ProjectId equals project.Id", reader, StringComparison.Ordinal);
        Assert.Contains("on run.Id equals operation.RunId into operationGroup", reader, StringComparison.Ordinal);
        Assert.Contains("RunExecutionMode.ManualAgent", reader, StringComparison.Ordinal);

        // The prepared commit identity exists in the projection only within an expression that also requires the Completed status.
        var occurrences = Regex.Matches(projection, @"row\.Operation\.CommitSha").Select(match => match.Index).ToArray();
        Assert.NotEmpty(occurrences);
        Assert.All(
            occurrences,
            index => Assert.Contains(
                "row.Operation.Status == LocalCommitStatus.Completed",
                projection[Math.Max(0, index - 160)..Math.Min(projection.Length, index + 160)],
                StringComparison.Ordinal));
    }

    [Fact]
    public void The_endpoint_dispatches_only_through_the_mediator_is_a_bodyless_authorized_get_and_answers_with_an_api_owned_graph()
    {
        var code = ApiCode();
        var endpoint = Assert.Single(Sources(ApiFolders), path => path.EndsWith("Endpoint.cs", StringComparison.Ordinal));
        var source = Code(endpoint);

        Assert.Contains(": RunsBaseEndpoint", source, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"\[HttpGet\("));
        Assert.Single(Regex.Matches(source, @"mediator\.SendAsync\("));
        Assert.Contains("[TypeFilter(typeof(LocalCommitInspectionGuardFilter))]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromBody]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[AllowAnonymous]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IArtifactStore", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeProvider", code, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalCommitOperationResponse", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestLocalCommit", code, StringComparison.Ordinal);
        foreach (var path in Sources(ApiFolders).Where(path => path.EndsWith("Response.cs", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("DevalCopilot.Application", Code(path), StringComparison.Ordinal);
            Assert.DoesNotContain("DevalCopilot.Domain", Code(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_body_and_query_guard_runs_before_binding_and_admits_no_query_at_all()
    {
        var guard = Code(Path.Combine(
            BackendRoot(), "DevalCopilot.Api", "Features", "Runs", "GetLocalCommitInspection", "LocalCommitInspectionGuardFilter.cs"));

        Assert.Contains(": IAsyncResourceFilter", guard, StringComparison.Ordinal);
        Assert.Contains("request.Query.Count > 0", guard, StringComparison.Ordinal);
        Assert.Contains("request.ContentLength is > 0", guard, StringComparison.Ordinal);
        Assert.Contains("HeaderNames.TransferEncoding", guard, StringComparison.Ordinal);
        Assert.Contains("request.Body.ReadAsync(new byte[1]", guard, StringComparison.Ordinal);
        Assert.Contains("LocalCommitInspectionErrors.QueryNotAllowed()", guard, StringComparison.Ordinal);
        Assert.Contains("LocalCommitInspectionErrors.BodyNotAllowed()", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("mediator", guard, StringComparison.OrdinalIgnoreCase);
    }
}
