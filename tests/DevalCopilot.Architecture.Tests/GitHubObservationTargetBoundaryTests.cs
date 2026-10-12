using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0040: recording a GitHub observation scope is local Human selection. The read side is persisted-row reading only; the
/// write side is one short write-locked transaction that adds exactly one target and one event; neither reaches Git, a process, the
/// network, a provider or a credential, and the name policy has one owner shared with the origin parser.</summary>
public sealed partial class GitHubObservationTargetBoundaryTests
{
    private const string Policies = "DevalCopilot.Application/Features/Runs/Policies/GitHubObservation";
    private const string SourceReader = Policies + "/GitHubObservationSourceReader.cs";
    private const string TargetReader = Policies + "/GitHubObservationTargetReader.cs";
    private const string Query = "DevalCopilot.Application/Features/Runs/Queries/GetGitHubObservationTarget/GetGitHubObservationTargetQueryHandler.cs";
    private const string Command = "DevalCopilot.Application/Features/Runs/Commands/RecordGitHubObservationTarget/RecordGitHubObservationTargetCommandHandler.cs";
    private const string PostEndpoint = "DevalCopilot.Api/Features/Runs/RecordGitHubObservationTarget/RecordGitHubObservationTargetEndpoint.cs";
    private const string GetEndpoint = "DevalCopilot.Api/Features/Runs/GetGitHubObservationTarget/GetGitHubObservationTargetEndpoint.cs";
    private const string Request = "DevalCopilot.Api/Features/Runs/RecordGitHubObservationTarget/RecordGitHubObservationTargetRequest.cs";
    private const string Parser = "DevalCopilot.Infrastructure/Features/Projects/GitHubOriginUrlParser.cs";
    private const string NamePolicy = "DevalCopilot.Domain/Features/Projects/GitHubRepositoryNamePolicy.cs";

    private static readonly string[] ExternalReaches =
    [
        "IArtifactStore", "IGit", "ILocalCommit", "IProcess", "Process", "IAgent", "File.", "Directory.", "Path.", "HttpClient", "Task.Delay",
        "IGitHub", "Credential", "Environment.", "Socket", "WebRequest",
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
    public void The_read_side_only_reads_untracked_rows_and_writes_locks_or_reaches_nothing_external()
    {
        foreach (var source in new[] { Query, SourceReader, TargetReader })
        {
            var code = CodeOf(source);

            Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
            Assert.DoesNotMatch(DbSetWrite(), code);
            foreach (var forbidden in new[]
            {
                "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "BeginTransaction", "IsModified", "AsTracking", "TimeProvider",
                "IRunEventNotifier", "RunEvent.Record", "OrderByDescending", "MaxAsync", "FirstOrDefault", "LastOrDefault", "IApplicationMediator",
            })
            {
                Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
            }

            foreach (var forbidden in ExternalReaches)
            {
                Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_source_is_the_unchanged_receipt_reader_plus_pinned_rows_and_the_branch_policy_and_never_the_client_or_head()
    {
        var code = CodeOf(SourceReader);

        Assert.Contains("new LocalDeliveryReceiptReader(dbContext)", code, StringComparison.Ordinal);
        Assert.Contains("LocalDeliveryReceiptState.Available", code, StringComparison.Ordinal);
        Assert.Contains("RunExecutionMode.ManualAgent", code, StringComparison.Ordinal);
        Assert.Contains("WorkspaceBranchNamePolicy.Compute(run.ProjectId, workspace.WorkspaceNumber)", code, StringComparison.Ordinal);
        Assert.Contains("candidate.Id == view.OperationId", code, StringComparison.Ordinal);
        Assert.Contains("candidate.Id == operation.GitWorkspaceId", code, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalCommitAuthorityReader", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GetLocalDeliveryReceiptQuery", code, StringComparison.Ordinal);
        Assert.DoesNotContain("HeadCommitSha", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_target_is_validated_together_with_its_source_and_its_single_human_event_from_bounded_projections()
    {
        var code = CodeOf(TargetReader);

        Assert.Contains("GitHubObservationTargetEventPayload.Build(target)", code, StringComparison.Ordinal);
        Assert.Contains("GitHubObservationTargetEventPayload.MaximumLength", code, StringComparison.Ordinal);
        Assert.Contains("ParticipantKind.Human", code, StringComparison.Ordinal);
        Assert.Contains("RunEventType.GitHubObservationTargetRecorded", code, StringComparison.Ordinal);
        Assert.Contains("GitHubObservationTarget.Record(", code, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(code, @"\.Take\(3\)").Count);
        Assert.Equal(2, Regex.Matches(code, @"\.Select\(candidate => new").Count);
    }

    [Fact]
    public void The_command_is_one_write_locked_transaction_that_adds_exactly_one_target_and_one_event_and_notifies_after_commit()
    {
        var code = CodeOf(Command);

        Assert.Single(Regex.Matches(code, @"BeginTransactionAsync\("));
        Assert.Single(Regex.Matches(code, @"SaveChangesAsync\("));
        Assert.Single(Regex.Matches(code, @"CommitAsync\("));
        Assert.Equal(2, Regex.Matches(code, @"\.Add\(").Count);
        Assert.Contains("dbContext.GitHubObservationTargets.Add(target)", code, StringComparison.Ordinal);
        Assert.Contains("dbContext.Events.Add(recordedEvent)", code, StringComparison.Ordinal);
        Assert.Contains("candidate.Lifecycle, candidate => candidate.Lifecycle", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"ExecuteUpdateAsync\("));
        Assert.DoesNotMatch(@"dbContext\.Runs\.(Add|Update|Attach|Remove)", code);
        Assert.DoesNotContain("ExecuteDelete", code, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (DbUpdate", code, StringComparison.Ordinal);
        Assert.Contains("ParticipantIdentity.ForHuman()", code, StringComparison.Ordinal);
        Assert.Contains("GitHubObservationTargetEventPayload.Build(target)", code, StringComparison.Ordinal);

        Assert.True(code.IndexOf("BeginTransactionAsync(", StringComparison.Ordinal) < code.IndexOf("ExecuteUpdateAsync(", StringComparison.Ordinal));
        Assert.True(code.IndexOf("ExecuteUpdateAsync(", StringComparison.Ordinal) < code.IndexOf("GitHubObservationSourceReader.ReadAsync(", StringComparison.Ordinal));
        Assert.True(code.IndexOf("GitHubObservationSourceReader.ReadAsync(", StringComparison.Ordinal) < code.IndexOf("SaveChangesAsync(", StringComparison.Ordinal));
        Assert.True(code.IndexOf("CommitAsync(", StringComparison.Ordinal) < code.IndexOf("await NotifyAsync(", StringComparison.Ordinal));

        foreach (var forbidden in ExternalReaches)
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        foreach (var untouched in new[] { "Lifecycle =", ".Claim(", ".Abandon(", "LastAdvancedAtUtc", "RepositoryMutationLeases", "GitWorkspaces", "Attempts" })
        {
            Assert.DoesNotContain(untouched, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_endpoints_are_protected_closed_and_only_send_their_message_through_the_mediator()
    {
        var root = BackendRoot();
        var post = CodeOf(PostEndpoint);
        var get = CodeOf(GetEndpoint);

        Assert.Contains("[HttpPost(\"{runId:guid}/github-observation-target\")]", post, StringComparison.Ordinal);
        Assert.Contains("[RequestSizeLimit(MaximumRequestBodyBytes)]", post, StringComparison.Ordinal);
        Assert.Contains("MaximumRequestBodyBytes = 8 * 1024", post, StringComparison.Ordinal);
        Assert.Contains("[FromBody] RecordGitHubObservationTargetRequest request", post, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"{runId:guid}/github-observation-target\")]", get, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromBody]", get, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromQuery]", get, StringComparison.Ordinal);
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
            SourcesContaining(root, "github-observation-target\")]"));
    }

    [Fact]
    public void The_request_admits_only_the_four_selection_fields_and_no_host_branch_url_or_credential()
    {
        var code = CodeOf(Request);

        Assert.Equal(
            ["CommitSha", "OperationId", "Owner", "Repository"],
            Regex.Matches(code, @"public required \w+ (\w+) \{ get; init; \}").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal));
        foreach (var forbidden in new[] { "Host", "Branch", "Url", "Uri", "Token", "Credential", "Password", "Secret" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_host_and_the_name_subset_each_have_one_owner_shared_with_the_origin_parser()
    {
        var root = BackendRoot();
        var parser = CodeOf(Parser);

        Assert.Contains("GitHubRepositoryNamePolicy.IsOwner(owner)", parser, StringComparison.Ordinal);
        Assert.Contains("GitHubRepositoryNamePolicy.IsRepository(repository)", parser, StringComparison.Ordinal);
        Assert.DoesNotContain("IsAsciiAlphanumeric", parser, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxOwnerLength", parser, StringComparison.Ordinal);
        Assert.Equal([NamePolicy], SourcesContaining(root, "IsAsciiAlphanumeric"));
        Assert.Contains("public const string Host = \"github.com\"", CodeOf("DevalCopilot.Domain/Features/Runs/GitHubObservationTarget.cs"), StringComparison.Ordinal);
        Assert.Equal(
            ["DevalCopilot.Domain/Features/Runs/GitHubObservationTarget.cs"],
            SourcesContaining(root, "Purpose = \"CiObservation\""));
        foreach (var slice in new[] { Query, SourceReader, TargetReader, Command, PostEndpoint, GetEndpoint, Request })
        {
            Assert.DoesNotContain("github.com", CodeOf(slice), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_event_type_is_written_by_exactly_one_handler_and_read_by_the_one_target_reader()
    {
        var root = BackendRoot();

        Assert.Equal(
            [
                "DevalCopilot.Domain/Features/Runs/RunEventType.cs",
            ],
            SourcesContaining(root, "\"run.github_observation_target_recorded\""));
        Assert.Equal(
            [Command, TargetReader],
            SourcesContaining(root, "RunEventType.GitHubObservationTargetRecorded"));
        Assert.Equal([Command], SourcesContaining(root, "dbContext.GitHubObservationTargets.Add("));
    }
}
