using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0048: a branch publication approval is a Human's recorded consent and nothing else. The read side is persisted-row reading
/// only; the write side reaches nothing external and runs one short write-locked transaction that rereads the whole scope, compares every
/// value and adds exactly one approval and one event; the approval table, its event type and its fixed contract each have one owner; the
/// unchanged ADR-0040 and ADR-0043 readers are the only source of scope.</summary>
public sealed partial class GitHubBranchPublicationApprovalBoundaryTests
{
    private const string Policies = "DevalCopilot.Application/Features/Runs/Policies/GitHubBranchPublicationApproval";
    private const string ScopeReader = Policies + "/GitHubBranchPublicationScopeReader.cs";
    private const string ScopeReading = Policies + "/GitHubBranchPublicationScopeReading.cs";
    private const string ApprovalReader = Policies + "/GitHubBranchPublicationApprovalReader.cs";
    private const string Query = "DevalCopilot.Application/Features/Runs/Queries/GetGitHubBranchPublicationApproval/GetGitHubBranchPublicationApprovalQueryHandler.cs";
    private const string Command = "DevalCopilot.Application/Features/Runs/Commands/RecordGitHubBranchPublicationApproval/RecordGitHubBranchPublicationApprovalCommandHandler.cs";
    private const string PostEndpoint = "DevalCopilot.Api/Features/Runs/RecordGitHubBranchPublicationApproval/RecordGitHubBranchPublicationApprovalEndpoint.cs";
    private const string GetEndpoint = "DevalCopilot.Api/Features/Runs/GetGitHubBranchPublicationApproval/GetGitHubBranchPublicationApprovalEndpoint.cs";
    private const string Request = "DevalCopilot.Api/Features/Runs/RecordGitHubBranchPublicationApproval/RecordGitHubBranchPublicationApprovalRequest.cs";
    private const string PostFilter = "DevalCopilot.Api/Features/Runs/RecordGitHubBranchPublicationApproval/StrictPublicationApprovalBodyFilter.cs";
    private const string GetFilter = "DevalCopilot.Api/Features/Runs/GetGitHubBranchPublicationApproval/PublicationApprovalReadGuardFilter.cs";
    private const string Entity = "DevalCopilot.Domain/Features/Runs/GitHubBranchPublicationApproval.cs";
    private const string Payload = "DevalCopilot.Domain/Features/Runs/GitHubBranchPublicationApprovalEventPayload.cs";
    private const string EventType = "DevalCopilot.Domain/Features/Runs/RunEventType.cs";

    private static readonly string[] ExternalReaches =
    [
        "IArtifactStore", "IGitWorkspace", "IGitRepository", "ILocalCommit", "IProcess", "Process", "IAgent", "File.", "Directory.", "Path.GetFull", "HttpClient",
        "Task.Delay", "Credential", "Environment.", "Socket", "WebRequest", "Observer", "HostCapabilitySnapshots", "GitHubCli",
    ];

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

    private static string[] SourcesContaining(string text) => ProductionSources(BackendRoot())
        .Where(path => Code(path).Contains(text, StringComparison.Ordinal))
        .Select(path => Path.GetRelativePath(BackendRoot(), path).Replace(Path.DirectorySeparatorChar, '/'))
        .Order(StringComparer.Ordinal)
        .ToArray();

    [GeneratedRegex(@"dbContext\.([A-Za-z]+)")]
    private static partial Regex DbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\s*\.AsNoTracking\(\)")]
    private static partial Regex UntrackedDbSetAccess();

    [Fact]
    public void The_read_side_only_reads_untracked_rows_and_never_writes_locks_or_reaches_anything_external()
    {
        foreach (var path in new[] { Query, ScopeReader, ApprovalReader })
        {
            var code = CodeOf(path);

            Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
            foreach (var forbidden in new[]
            {
                "SaveChanges", ".Add(", ".Remove(", ".Update(", "ExecuteUpdate", "ExecuteDelete", "ExecuteSql", "FromSql", "Attach(",
                "BeginTransaction", "AsTracking", "IRunEventNotifier", "RunEvent.Record", "TimeProvider", "IApplicationMediator",
            })
            {
                Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
            }

            foreach (var reach in ExternalReaches)
            {
                Assert.DoesNotContain(reach, code, StringComparison.Ordinal);
            }
        }

        Assert.Equal(
            ["Events", "GitHubBranchPublicationApprovals"],
            DbSetAccess().Matches(CodeOf(ApprovalReader)).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_scope_comes_only_from_the_three_unchanged_readers_through_one_scope_reader_and_the_binding_reader_gets_the_coherent_target()
    {
        var scope = CodeOf(ScopeReader);

        Assert.Single(Regex.Matches(scope, @"GitHubObservationSourceReader\.ReadAsync\("));
        Assert.Single(Regex.Matches(scope, @"GitHubObservationTargetReader\.ReadAsync\("));
        Assert.Single(Regex.Matches(scope, @"GitHubRepositoryIdentityBindingReader\.ReadAsync\("));
        Assert.Contains("target.Kind == GitHubObservationTargetReadingKind.Coherent ? target.Target : null", scope, StringComparison.Ordinal);
        Assert.Equal([ScopeReader], SourcesContaining("GitHubObservationSourceReader.ReadAsync(").Where(path => path.Contains("BranchPublication", StringComparison.Ordinal)));
        Assert.Equal(
            [Command, Query],
            SourcesContaining("GitHubBranchPublicationScopeReader.ReadAsync("));
        Assert.Equal(
            [Command, Query],
            SourcesContaining("GitHubBranchPublicationApprovalReader.ReadAsync("));
        Assert.Contains("Scope =>", CodeOf(ScopeReading), StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_runs_nothing_external_and_rereads_and_compares_the_whole_scope_under_one_write_lock()
    {
        var code = CodeOf(Command);

        Assert.Single(Regex.Matches(code, @"BeginTransactionAsync\("));
        Assert.Single(Regex.Matches(code, @"ExecuteUpdateAsync\("));
        Assert.Single(Regex.Matches(code, @"SaveChangesAsync\("));
        Assert.Single(Regex.Matches(code, @"CommitAsync\("));
        Assert.Equal(2, Regex.Matches(code, @"await ReadAsync\(").Count);
        Assert.Equal(2, Regex.Matches(code, @"\.Add\(").Count);
        Assert.Contains("dbContext.GitHubBranchPublicationApprovals.Add(approval)", code, StringComparison.Ordinal);
        Assert.Contains("dbContext.Events.Add(recordedEvent)", code, StringComparison.Ordinal);
        Assert.Contains("candidate.Lifecycle, candidate => candidate.Lifecycle", code, StringComparison.Ordinal);
        Assert.Contains("after.Scope != before.Scope", code, StringComparison.Ordinal);
        Assert.Contains("candidate.Id == command.ApprovalId && candidate.RunId != command.RunId", code, StringComparison.Ordinal);
        Assert.Contains("approvedAtUtc < scope.Binding.RecordedAtUtc", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"dbContext\.Runs\.(Add|Update|Attach|Remove)", code);
        Assert.DoesNotContain("ExecuteDelete", code, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (DbUpdate", code, StringComparison.Ordinal);
        Assert.Contains("ParticipantIdentity.ForHuman()", code, StringComparison.Ordinal);
        Assert.Contains("GitHubBranchPublicationApprovalEventPayload.Build(approval)", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"timeProvider\.GetUtcNow\(\)"));

        var firstRead = code.IndexOf("await ReadAsync(", StringComparison.Ordinal);
        var begin = code.IndexOf("BeginTransactionAsync(", StringComparison.Ordinal);
        var lockStatement = code.IndexOf("ExecuteUpdateAsync(", StringComparison.Ordinal);
        var secondRead = code.LastIndexOf("await ReadAsync(", StringComparison.Ordinal);
        var stamp = code.IndexOf("timeProvider.GetUtcNow()", StringComparison.Ordinal);
        var save = code.IndexOf("SaveChangesAsync(", StringComparison.Ordinal);
        var commit = code.IndexOf("CommitAsync(", StringComparison.Ordinal);
        var notify = code.IndexOf("await NotifyAsync(", StringComparison.Ordinal);
        Assert.True(firstRead < begin && begin < lockStatement && lockStatement < secondRead);
        Assert.True(secondRead < stamp && stamp < save && save < commit && commit < notify);

        foreach (var reach in ExternalReaches)
        {
            Assert.DoesNotContain(reach, code, StringComparison.Ordinal);
        }

        foreach (var untouched in new[] { "Lifecycle =", ".Claim(", ".Abandon(", "LastAdvancedAtUtc", "RepositoryMutationLeases", "GitWorkspaces", "Attempts", "LocalCommitOperations" })
        {
            Assert.DoesNotContain(untouched, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_endpoints_are_protected_closed_and_only_send_their_message_through_the_mediator()
    {
        var post = CodeOf(PostEndpoint);
        var get = CodeOf(GetEndpoint);

        Assert.Contains("[HttpPost(\"{runId:guid}/github-branch-publication-approval\")]", post, StringComparison.Ordinal);
        Assert.Contains("[RequestSizeLimit(MaximumRequestBodyBytes)]", post, StringComparison.Ordinal);
        Assert.Contains("MaximumRequestBodyBytes = 8 * 1024", post, StringComparison.Ordinal);
        Assert.Contains("[TypeFilter(typeof(StrictPublicationApprovalBodyFilter))]", post, StringComparison.Ordinal);
        Assert.Contains("[FromBody] RecordGitHubBranchPublicationApprovalRequest request", post, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"{runId:guid}/github-branch-publication-approval\")]", get, StringComparison.Ordinal);
        Assert.Contains("[TypeFilter(typeof(PublicationApprovalReadGuardFilter))]", get, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromBody]", get, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromQuery]", get, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromQuery]", post, StringComparison.Ordinal);
        foreach (var code in new[] { post, get })
        {
            Assert.Contains(": RunsBaseEndpoint", code, StringComparison.Ordinal);
            Assert.DoesNotContain("AllowAnonymous", code, StringComparison.Ordinal);
            Assert.DoesNotContain("dbContext", code, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DevalCopilot.Domain", code, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(code, @"mediator\.SendAsync\("));
        }

        Assert.Equal(
            [GetEndpoint, PostEndpoint],
            SourcesContaining("github-branch-publication-approval\")]"));
    }

    [Fact]
    public void The_request_admits_only_the_seven_acknowledgements_and_both_filters_refuse_everything_else_before_dispatch()
    {
        var request = CodeOf(Request);
        var post = CodeOf(PostFilter);
        var get = CodeOf(GetFilter);

        Assert.Equal(
            ["ApprovalId", "BindingId", "BranchName", "CommitSha", "OperationId", "RepositoryNodeId", "TargetId"],
            Regex.Matches(request, @"public required \w+ (\w+) \{ get; init; \}").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal));
        foreach (var forbidden in new[] { "Host", "Url", "Uri", "Owner", "Repository ", "Token", "Credential", "Password", "Secret", "Refspec", "Message", "Action", "Text", "Utc" })
        {
            Assert.DoesNotContain(forbidden, request, StringComparison.Ordinal);
        }

        Assert.Contains("ValueIsEscaped", post, StringComparison.Ordinal);
        Assert.Contains("seen.ContainsKey(name)", post, StringComparison.Ordinal);
        Assert.Contains("seen.Count != Members.Length", post, StringComparison.Ordinal);
        Assert.Contains("MaximumBodyBytes = 8 * 1024", post, StringComparison.Ordinal);
        Assert.Contains("new byte[MaximumBodyBytes + 1]", post, StringComparison.Ordinal);
        Assert.Contains("seen[member].Length == 36", post, StringComparison.Ordinal);
        Assert.Contains("request.Query.Count > 0", post, StringComparison.Ordinal);
        Assert.Contains("request.Query.Count > 0", get, StringComparison.Ordinal);
        Assert.Contains("HeaderNames.TransferEncoding", get, StringComparison.Ordinal);
        foreach (var code in new[] { post, get })
        {
            Assert.DoesNotContain("dbContext", code, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mediator", code, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DevalCopilot.Domain", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_shared_views_live_in_their_policy_folder_and_the_api_graph_is_its_own()
    {
        var view = Policies + "/GitHubBranchPublicationApprovalView.cs";
        var response = "DevalCopilot.Api/Features/Runs/GetGitHubBranchPublicationApproval/GitHubBranchPublicationApprovalResponse.cs";

        Assert.Equal([view], SourcesContaining("public sealed record GitHubBranchPublicationApprovalView("));
        Assert.Equal([response], SourcesContaining("public sealed record GitHubBranchPublicationApprovalResponse("));
        Assert.Contains("namespace DevalCopilot.Application.Features.Runs.Policies.GitHubBranchPublicationApproval;", CodeOf(view), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(BackendRoot(), "DevalCopilot.Application", "Features", "Runs", "GitHubBranchPublicationApprovalView.cs")));
        Assert.False(File.Exists(Path.Combine(BackendRoot(), "DevalCopilot.Api", "Features", "Runs", "GitHubBranchPublicationApprovalResponse.cs")));

        foreach (var path in SourcesContaining("GitHubBranchPublicationApproval").Where(path => path.StartsWith("DevalCopilot.Api/", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("GitHubObservationSourceResponse", CodeOf(path), StringComparison.Ordinal);
            Assert.DoesNotContain("GitHubObservationScopeResponse", CodeOf(path), StringComparison.Ordinal);
            Assert.DoesNotContain("GitHubRepositoryIdentityBindingResponse", CodeOf(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_table_the_event_type_and_the_fixed_contract_each_have_one_owner_and_the_ci_purpose_is_untouched()
    {
        Assert.Equal([EventType], SourcesContaining("\"run.github_branch_publication_approved\""));
        Assert.Equal([Command, ApprovalReader], SourcesContaining("RunEventType.GitHubBranchPublicationApproved"));
        Assert.Equal([Command], SourcesContaining("dbContext.GitHubBranchPublicationApprovals.Add("));
        Assert.Equal([Command, ApprovalReader], SourcesContaining("dbContext.GitHubBranchPublicationApprovals"));
        Assert.Equal([Entity], SourcesContaining("\"PushExactCommit\""));
        Assert.Equal([Payload], SourcesContaining("GitHubBranchPublicationApproval.PolicyVersion").Where(path => path.StartsWith("DevalCopilot.Domain/", StringComparison.Ordinal)));

        foreach (var path in SourcesContaining("GitHubBranchPublicationApproval").Concat(SourcesContaining("github-branch-publication-approval")).Distinct())
        {
            var code = CodeOf(path);
            Assert.DoesNotContain("CiObservation", code, StringComparison.Ordinal);
            Assert.DoesNotContain("GitHubObservationTarget.Purpose", code, StringComparison.Ordinal);
        }

        Assert.Contains("public const string Purpose = \"CiObservation\";", CodeOf("DevalCopilot.Domain/Features/Runs/GitHubObservationTarget.cs"), StringComparison.Ordinal);
    }
}
