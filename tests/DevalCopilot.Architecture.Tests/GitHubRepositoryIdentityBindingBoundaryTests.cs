using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0043: binding a repository identity is a Human confirmation of one fresh host observation. The read side is persisted-row
/// reading only; the write side observes exactly once with no transaction open and then runs one short write-locked transaction that
/// rereads the whole scope and adds exactly one binding and one event; the identity port and the node-identifier rule each have one owner
/// shared with ADR-0041 and ADR-0042, which stay unchanged.</summary>
public sealed partial class GitHubRepositoryIdentityBindingBoundaryTests
{
    private const string Policies = "DevalCopilot.Application/Features/Runs/Policies/GitHubObservation";
    private const string Reader = Policies + "/GitHubRepositoryIdentityBindingReader.cs";
    private const string Query = "DevalCopilot.Application/Features/Runs/Queries/GetGitHubRepositoryIdentityBinding/GetGitHubRepositoryIdentityBindingQueryHandler.cs";
    private const string Command = "DevalCopilot.Application/Features/Runs/Commands/BindGitHubRepositoryIdentity/BindGitHubRepositoryIdentityCommandHandler.cs";
    private const string PostEndpoint = "DevalCopilot.Api/Features/Runs/BindGitHubRepositoryIdentity/BindGitHubRepositoryIdentityEndpoint.cs";
    private const string GetEndpoint = "DevalCopilot.Api/Features/Runs/GetGitHubRepositoryIdentityBinding/GetGitHubRepositoryIdentityBindingEndpoint.cs";
    private const string Request = "DevalCopilot.Api/Features/Runs/BindGitHubRepositoryIdentity/BindGitHubRepositoryIdentityRequest.cs";
    private const string Filter = "DevalCopilot.Api/Features/Runs/BindGitHubRepositoryIdentity/StrictBindingBodyFilter.cs";
    private const string Port = "DevalCopilot.Application/Features/Runs/Ports/IGitHubRepositoryIdentityObserver.cs";
    private const string PortResult = "DevalCopilot.Application/Features/Runs/Ports/GitHubRepositoryIdentity.cs";
    private const string NodeIdPolicy = "DevalCopilot.Domain/Features/Projects/GitHubRepositoryNodeIdPolicy.cs";

    private static readonly string[] ExternalReaches =
    [
        "IArtifactStore", "IGitWorkspace", "IGitRepository", "ILocalCommit", "IProcess", "Process", "IAgent", "File.", "Directory.", "Path.GetFull", "HttpClient",
        "Task.Delay", "Credential", "Environment.", "Socket", "WebRequest",
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
    public void The_read_side_only_reads_untracked_rows_and_never_writes_locks_or_reaches_a_process_or_the_network()
    {
        foreach (var path in new[] { Query, Reader })
        {
            var code = CodeOf(path);

            Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
            foreach (var forbidden in new[]
            {
                "SaveChanges", ".Add(", ".Remove(", ".Update(", "ExecuteUpdate", "ExecuteDelete", "ExecuteSql", "FromSql", "Attach(",
                "BeginTransaction", "AsTracking", "IRunEventNotifier", "RunEvent.Record", "IGitHubRepositoryIdentityObserver", "ObserveAsync",
                "TimeProvider", "IApplicationMediator",
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
            ["Events", "GitHubRepositoryIdentityBindings"],
            DbSetAccess().Matches(CodeOf(Reader)).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_scope_comes_only_from_the_unchanged_readers_and_the_binding_reader_gets_the_coherent_target()
    {
        var command = CodeOf(Command);
        var query = CodeOf(Query);

        foreach (var code in new[] { command, query })
        {
            Assert.Single(Regex.Matches(code, @"GitHubObservationSourceReader\.ReadAsync\("));
            Assert.Single(Regex.Matches(code, @"GitHubObservationTargetReader\.ReadAsync\("));
            Assert.Single(Regex.Matches(code, @"GitHubRepositoryIdentityBindingReader\.ReadAsync\("));
            Assert.Contains("GitHubObservationTargetReadingKind.Coherent ? targetReading.Target : null", code, StringComparison.Ordinal);
        }

        Assert.Equal(
            [Command, "DevalCopilot.Application/Features/Runs/Queries/GetGitHubCiObservation/GetGitHubCiObservationQueryHandler.cs", Query],
            SourcesContaining("GitHubRepositoryIdentityBindingReader.ReadAsync("));
    }

    [Fact]
    public void The_command_observes_exactly_once_with_no_transaction_open_and_then_rereads_the_scope_under_one_write_lock()
    {
        var code = CodeOf(Command);

        Assert.Single(Regex.Matches(code, @"observer\.ObserveAsync\("));
        Assert.Single(Regex.Matches(code, @"BeginTransactionAsync\("));
        Assert.Single(Regex.Matches(code, @"ExecuteUpdateAsync\("));
        Assert.Single(Regex.Matches(code, @"SaveChangesAsync\("));
        Assert.Single(Regex.Matches(code, @"CommitAsync\("));
        Assert.Equal(2, Regex.Matches(code, @"await ReadScopeAsync\(").Count);
        Assert.Equal(2, Regex.Matches(code, @"\.Add\(").Count);
        Assert.Contains("dbContext.GitHubRepositoryIdentityBindings.Add(binding)", code, StringComparison.Ordinal);
        Assert.Contains("dbContext.Events.Add(recordedEvent)", code, StringComparison.Ordinal);
        Assert.Contains("candidate.Lifecycle, candidate => candidate.Lifecycle", code, StringComparison.Ordinal);
        Assert.Contains("after.Scope != before.Scope", code, StringComparison.Ordinal);
        Assert.Contains("HostCapabilityReadinessProjector.IsStale", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"dbContext\.Runs\.(Add|Update|Attach|Remove)", code);
        Assert.DoesNotContain("ExecuteDelete", code, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (DbUpdate", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ObserveAsync(CancellationToken.None", code, StringComparison.Ordinal);
        Assert.Contains("ParticipantIdentity.ForHuman()", code, StringComparison.Ordinal);
        Assert.Contains("GitHubRepositoryIdentityBindingEventPayload.Build(binding)", code, StringComparison.Ordinal);

        var firstRead = code.IndexOf("await ReadScopeAsync(", StringComparison.Ordinal);
        var observe = code.IndexOf("await ObserveAsync(", StringComparison.Ordinal);
        var begin = code.IndexOf("BeginTransactionAsync(", StringComparison.Ordinal);
        var lockStatement = code.IndexOf("ExecuteUpdateAsync(", StringComparison.Ordinal);
        var secondRead = code.LastIndexOf("await ReadScopeAsync(", StringComparison.Ordinal);
        var stamp = code.IndexOf("timeProvider.GetUtcNow()", code.IndexOf("after.Scope", StringComparison.Ordinal), StringComparison.Ordinal);
        var save = code.IndexOf("SaveChangesAsync(", StringComparison.Ordinal);
        var commit = code.IndexOf("CommitAsync(", StringComparison.Ordinal);
        var notify = code.IndexOf("await NotifyAsync(", StringComparison.Ordinal);
        Assert.True(firstRead < observe && observe < begin && begin < lockStatement && lockStatement < secondRead);
        Assert.True(secondRead < stamp && stamp < save && save < commit && commit < notify);

        foreach (var reach in ExternalReaches)
        {
            Assert.DoesNotContain(reach, code, StringComparison.Ordinal);
        }

        foreach (var untouched in new[] { "Lifecycle =", ".Claim(", ".Abandon(", "LastAdvancedAtUtc", "RepositoryMutationLeases", "GitWorkspaces", "Attempts" })
        {
            Assert.DoesNotContain(untouched, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_endpoints_are_protected_closed_and_only_send_their_message_through_the_mediator()
    {
        var post = CodeOf(PostEndpoint);
        var get = CodeOf(GetEndpoint);

        Assert.Contains("[HttpPost(\"{runId:guid}/github-repository-identity-binding\")]", post, StringComparison.Ordinal);
        Assert.Contains("[RequestSizeLimit(MaximumRequestBodyBytes)]", post, StringComparison.Ordinal);
        Assert.Contains("MaximumRequestBodyBytes = 8 * 1024", post, StringComparison.Ordinal);
        Assert.Contains("[TypeFilter(typeof(StrictBindingBodyFilter))]", post, StringComparison.Ordinal);
        Assert.Contains("[FromBody] BindGitHubRepositoryIdentityRequest request", post, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"{runId:guid}/github-repository-identity-binding\")]", get, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromBody]", get, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromQuery]", get, StringComparison.Ordinal);
        Assert.Contains("HasBodyAsync(cancellationToken)", get, StringComparison.Ordinal);
        Assert.True(get.IndexOf("HasBodyAsync(cancellationToken)", StringComparison.Ordinal) < get.IndexOf("mediator.SendAsync(", StringComparison.Ordinal));
        foreach (var code in new[] { post, get })
        {
            Assert.Contains(": RunsBaseEndpoint", code, StringComparison.Ordinal);
            Assert.DoesNotContain("AllowAnonymous", code, StringComparison.Ordinal);
            Assert.DoesNotContain("dbContext", code, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DevalCopilot.Domain", code, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(code, @"mediator\.SendAsync\("));
        }

        Assert.Equal(
            [PostEndpoint, GetEndpoint],
            SourcesContaining("github-repository-identity-binding\")]"));
    }

    [Fact]
    public void The_request_admits_only_the_four_confirmation_fields_and_the_filter_refuses_everything_else_before_dispatch()
    {
        var request = CodeOf(Request);
        var filter = CodeOf(Filter);

        Assert.Equal(
            ["CommitSha", "OperationId", "RepositoryNodeId", "TargetId"],
            Regex.Matches(request, @"public required \w+ (\w+) \{ get; init; \}").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal));
        foreach (var forbidden in new[] { "Host", "Branch", "Url", "Uri", "Owner", "Repository ", "Token", "Credential", "Password", "Secret", "Observed" })
        {
            Assert.DoesNotContain(forbidden, request, StringComparison.Ordinal);
        }

        Assert.Contains("AllowDuplicateProperties = false", filter, StringComparison.Ordinal);
        Assert.Contains("properties.Length != Members.Length", filter, StringComparison.Ordinal);
        Assert.Contains("StringComparison.Ordinal", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext", filter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mediator", filter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_identity_port_and_the_node_identifier_rule_each_have_one_owner_shared_by_both_observations_and_the_binding()
    {
        Assert.Equal([Port], SourcesContaining("public interface IGitHubRepositoryIdentityObserver"));
        Assert.Equal([PortResult], SourcesContaining("public sealed record GitHubRepositoryIdentity("));
        Assert.Equal([NodeIdPolicy], SourcesContaining("public static class GitHubRepositoryNodeIdPolicy"));
        Assert.Contains(
            "namespace DevalCopilot.Application.Features.Runs.Ports;", CodeOf(Port), StringComparison.Ordinal);
        Assert.Contains(
            "namespace DevalCopilot.Domain.Features.Projects;", CodeOf(NodeIdPolicy), StringComparison.Ordinal);
        Assert.Contains("GitHubRepositoryNodeIdPolicy.IsNodeId(", CodeOf(Command), StringComparison.Ordinal);
        Assert.Contains("GitHubRepositoryNodeIdPolicy.MaximumLength", CodeOf(Reader), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "GitHubRepositoryIdentityAdapter", CodeOf(Command), StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_binding_contract_types_live_with_their_owner_and_not_in_the_feature_roots()
    {
        var view = "DevalCopilot.Application/Features/Runs/Policies/GitHubObservation/GitHubRepositoryIdentityBindingView.cs";
        var response = "DevalCopilot.Api/Features/Runs/GetGitHubRepositoryIdentityBinding/GitHubRepositoryIdentityBindingResponse.cs";

        Assert.Equal([view], SourcesContaining("public sealed record GitHubRepositoryIdentityBindingView("));
        Assert.Equal([response], SourcesContaining("public sealed record GitHubRepositoryIdentityBindingResponse("));
        Assert.Contains("namespace DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;", CodeOf(view), StringComparison.Ordinal);
        Assert.Contains("namespace DevalCopilot.Api.Features.Runs.GetGitHubRepositoryIdentityBinding;", CodeOf(response), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(BackendRoot(), "DevalCopilot.Application", "Features", "Runs", "GitHubRepositoryIdentityBindingView.cs")));
        Assert.False(File.Exists(Path.Combine(BackendRoot(), "DevalCopilot.Api", "Features", "Runs", "GitHubRepositoryIdentityBindingResponse.cs")));
    }

    [Fact]
    public void The_event_type_is_written_by_exactly_one_handler_and_read_by_the_one_binding_reader()
    {
        Assert.Equal(["DevalCopilot.Domain/Features/Runs/RunEventType.cs"], SourcesContaining("\"run.github_repository_identity_bound\""));
        Assert.Equal([Command, Reader], SourcesContaining("RunEventType.GitHubRepositoryIdentityBound"));
        Assert.Equal([Command], SourcesContaining("dbContext.GitHubRepositoryIdentityBindings.Add("));
    }
}
