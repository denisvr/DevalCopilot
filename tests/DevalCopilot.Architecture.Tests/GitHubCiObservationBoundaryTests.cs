using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0044: the commit check observation is one protected, bodyless mediator query that reads the recorded scope, binding and
/// the GitHubCli snapshot untracked, reads the whole scope and binding again after the external call, never writes, locks or opens a
/// transaction, never touches Domain or persistence, and delegates to one narrow adapter that composes exactly one fixed read-only
/// GraphQL invocation of the vetted executable on the existing process engine with raw-string variables, no mutation, shell, jq, retry
/// or swallowed unexpected fault.</summary>
public sealed partial class GitHubCiObservationBoundaryTests
{
    private const string QueryFolder = "DevalCopilot.Application/Features/Runs/Queries/GetGitHubCiObservation";
    private const string Handler = QueryFolder + "/GetGitHubCiObservationQueryHandler.cs";
    private const string AdapterPath = "DevalCopilot.Infrastructure/Features/Runs/GitHubCiObservationAdapter.cs";
    private const string Endpoint = "DevalCopilot.Api/Features/Runs/GetGitHubCiObservation/GetGitHubCiObservationEndpoint.cs";
    private const string NodeIdPolicy = "DevalCopilot.Domain/Features/Projects/GitHubRepositoryNodeIdPolicy.cs";
    private const string BranchPolicy = "DevalCopilot.Application/Features/Runs/Queries/GetGitHubBranchObservation/GitHubOwnedBranchNamePolicy.cs";
    private const string RollupPolicy = QueryFolder + "/GitHubCiRollupStatePolicy.cs";

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

    private static string QueryCode()
    {
        var folder = Path.Combine(BackendRoot(), QueryFolder.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(folder), "The commit check observation must live in its own operation folder.");
        return string.Join('\n', Directory.EnumerateFiles(folder, "*.cs").Order(StringComparer.Ordinal).Select(Code));
    }

    [GeneratedRegex(@"dbContext\.([A-Za-z]+)")]
    private static partial Regex DbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\s*\.AsNoTracking\(\)")]
    private static partial Regex UntrackedDbSetAccess();

    [Fact]
    public void The_query_only_reads_untracked_rows_and_never_writes_locks_or_reaches_a_process_or_the_network_itself()
    {
        var code = QueryCode();

        Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
        Assert.Equal(
            ["HostCapabilitySnapshots", "Runs"],
            DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in new[]
        {
            "SaveChanges", ".Add(", ".Remove(", ".Update(", "ExecuteUpdate", "ExecuteDelete", "ExecuteSql", "FromSql", "Attach(",
            "BeginTransaction", "AsTracking", "IRunEventNotifier", "RunEvent.Record", "System.IO", "Process", "Http", "Task.Run",
            "Task.Delay", "Environment.", "Credential", "catch", "IApplicationMediator", "IGitWorkspace", "IGitRepository", "IAgent",
            "IManualTransactionCommand", "lock (", "SemaphoreSlim",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_scope_and_binding_come_only_from_the_unchanged_readers_and_are_read_again_after_the_single_external_call()
    {
        var code = CodeOf(Handler);

        Assert.Equal(2, Regex.Matches(code, @"await ReadScopeAsync\(").Count);
        Assert.Single(Regex.Matches(code, @"GitHubObservationSourceReader\.ReadAsync\("));
        Assert.Single(Regex.Matches(code, @"GitHubObservationTargetReader\.ReadAsync\("));
        Assert.Single(Regex.Matches(code, @"GitHubRepositoryIdentityBindingReader\.ReadAsync\("));
        Assert.Single(Regex.Matches(code, @"observer\.ObserveAsync\("));
        Assert.Contains("HostCapabilityReadinessProjector.IsStale", code, StringComparison.Ordinal);
        Assert.Contains("after != scope", code, StringComparison.Ordinal);

        var firstRead = code.IndexOf("await ReadScopeAsync(", StringComparison.Ordinal);
        var usable = code.IndexOf("IsUsableTarget(snapshot", StringComparison.Ordinal);
        var observe = code.IndexOf("observer.ObserveAsync(", StringComparison.Ordinal);
        var secondRead = code.LastIndexOf("await ReadScopeAsync(", StringComparison.Ordinal);
        var validate = code.IndexOf("IsAcceptedProbe(probe", StringComparison.Ordinal);
        var identity = code.IndexOf("IdentityChanged", code.IndexOf("IsAcceptedProbe(probe", StringComparison.Ordinal), StringComparison.Ordinal);
        var stamp = code.IndexOf("timeProvider.GetUtcNow()", observe, StringComparison.Ordinal);
        var observed = code.IndexOf("new GitHubCiObserved(", StringComparison.Ordinal);
        Assert.True(
            firstRead < usable && usable < observe && observe < secondRead && secondRead < validate && validate < identity
            && identity < stamp && stamp < observed);
        foreach (var forbidden in new[] { "ObserveAsync(CancellationToken.None", "GetUtcNow().UtcDateTime", "Math.Max", "DateTimeOffset.UtcNow" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        Assert.Contains("observedAt < binding.RecordedAtUtc", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_endpoint_is_protected_bodyless_and_refuses_a_body_before_anything_reaches_the_mediator()
    {
        var code = CodeOf(Endpoint);

        Assert.Contains("[HttpGet(\"{runId:guid}/github-ci-observation\")]", code, StringComparison.Ordinal);
        Assert.Contains(": RunsBaseEndpoint", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromBody]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromQuery]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPost", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnonymous", code, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DevalCopilot.Domain", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"mediator\.SendAsync\("));
        Assert.True(
            code.IndexOf("HasBodyAsync(cancellationToken)", StringComparison.Ordinal)
            < code.IndexOf("mediator.SendAsync(", StringComparison.Ordinal));
        Assert.Contains("request.ContentLength is > 0", code, StringComparison.Ordinal);
        Assert.Contains("HeaderNames.TransferEncoding", code, StringComparison.Ordinal);
        Assert.Equal([Endpoint], SourcesContaining("github-ci-observation\")]"));
    }

    [Fact]
    public void The_adapter_composes_exactly_one_fixed_read_only_graphql_invocation_on_the_existing_process_engine()
    {
        var code = CodeOf(AdapterPath);

        Assert.Single(Regex.Matches(code, @"_processExecutionAdapter\.ExecuteAsync\("));
        Assert.Contains("\"api\", \"graphql\", \"--hostname\", \"github.com\", \"--method\", \"POST\"", code, StringComparison.Ordinal);
        Assert.Contains(
            "\"-f\", \"query=\" + Query, \"-f\", \"owner=\" + owner, \"-f\", \"name=\" + repository, \"-f\", \"ref=refs/heads/\" + branchName", code,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner \"",
            code, StringComparison.Ordinal);
        Assert.Contains(
            "\"ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid statusCheckRollup{state}}}}}}\"", code,
            StringComparison.Ordinal);
        Assert.Contains("_probeSlot.Wait(0, CancellationToken.None)", code, StringComparison.Ordinal);
        Assert.Contains("_probeSlot.Release()", code, StringComparison.Ordinal);
        Assert.True(code.IndexOf("finally", StringComparison.Ordinal) < code.IndexOf("_probeSlot.Release()", StringComparison.Ordinal));
        foreach (var forbidden in new[]
        {
            "\"-F\"", "--jq", "--paginate", "--input", "--header", "--cache", "--template", "--silent", "--verbose", "mutation", "subscription",
            "cmd", "powershell", "pwsh", "bash", "Task.Delay", "Retry", "Shell", "UseShellExecute", "File.", "Directory.Create", "Path.Combine",
            "StandardOutputSinkPath", "StandardErrorSinkPath", "StandardInput", "catch (Exception", "catch (InvalidOperationException",
            "catch\n", "$\"", "string.Format", "StringBuilder", "contexts", "checkSuites", "checkRuns", "workflowRun", "pullRequest",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        Assert.Contains(AdapterPath, SourcesContaining("\"graphql\""));
        Assert.Equal(
            ["DevalCopilot.Infrastructure/Features/Runs/GitHubCheckContextsObservationAdapter.cs", AdapterPath],
            SourcesContaining("statusCheckRollup"));
    }

    [Fact]
    public void The_branch_node_id_and_rollup_rules_have_one_owner_each_shared_by_the_adapter_and_the_handler()
    {
        Assert.Equal([NodeIdPolicy], SourcesContaining("public static class GitHubRepositoryNodeIdPolicy"));
        Assert.Equal([BranchPolicy], SourcesContaining("public static class GitHubOwnedBranchNamePolicy"));
        Assert.Equal([RollupPolicy], SourcesContaining("public static class GitHubCiRollupStatePolicy"));
        foreach (var slice in new[] { AdapterPath, Handler })
        {
            var code = CodeOf(slice);
            Assert.Contains("GitHubRepositoryNodeIdPolicy.IsNodeId(", code, StringComparison.Ordinal);
            Assert.Contains("GitHubOwnedBranchNamePolicy.IsOwnedBranch(", code, StringComparison.Ordinal);
            Assert.Contains("GitHubObservationTarget.IsCommitSha(", code, StringComparison.Ordinal);
            Assert.Contains("GitHubCiRollupStatePolicy.IsRollupState(", code, StringComparison.Ordinal);
            Assert.Contains("GitHubRepositoryNamePolicy.IsOwner(", code, StringComparison.Ordinal);
            Assert.Contains("GitHubRepositoryNamePolicy.IsRepository(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("IsAsciiAlphanumeric", code, StringComparison.Ordinal);
        }

        foreach (var slice in new[] { Handler, AdapterPath, Endpoint })
        {
            Assert.DoesNotContain("github.com\"", CodeOf(slice).Replace("\"--hostname\", \"github.com\"", string.Empty), StringComparison.OrdinalIgnoreCase);
        }

        // Only the five literal states exist, once, in the policy; nothing translates them.
        var policy = CodeOf(RollupPolicy);
        Assert.Contains("\"ERROR\" or \"EXPECTED\" or \"FAILURE\" or \"PENDING\" or \"SUCCESS\"", policy, StringComparison.Ordinal);
        foreach (var translated in new[] { "Passed", "Approved", "Ready", "Green" })
        {
            var word = new Regex($@"\b{translated}\b");
            Assert.DoesNotMatch(word, QueryCode());
            Assert.DoesNotMatch(word, CodeOf(AdapterPath));
            Assert.DoesNotMatch(word, CodeOf(Endpoint));
        }
    }

    [Fact]
    public void The_feature_has_no_domain_or_persistence_footprint_and_exactly_the_documented_production_files()
    {
        var expected = new[]
        {
            "DevalCopilot.Api/Features/Runs/GetGitHubCiObservation/GetGitHubCiObservationEndpoint.cs",
            "DevalCopilot.Api/Features/Runs/GetGitHubCiObservation/GetGitHubCiObservationResponse.cs",
            "DevalCopilot.Api/Features/Runs/GetGitHubCiObservation/GitHubCiObservationResponse.cs",
            "DevalCopilot.Api/Features/Runs/GetGitHubCiObservation/GitHubCiRefResponse.cs",
            "DevalCopilot.Api/Program.cs",
            "DevalCopilot.Application/Features/Runs/Errors/GitHubCiObservationErrors.cs",
            QueryFolder + "/GetGitHubCiObservationQuery.cs",
            QueryFolder + "/GetGitHubCiObservationQueryHandler.cs",
            QueryFolder + "/GetGitHubCiObservationQueryResult.cs",
            QueryFolder + "/GitHubCiObservationStatus.cs",
            QueryFolder + "/GitHubCiObserved.cs",
            QueryFolder + "/GitHubCiProbe.cs",
            QueryFolder + "/GitHubCiRef.cs",
            QueryFolder + "/GitHubCiRollupStatePolicy.cs",
            QueryFolder + "/IGitHubCiObserver.cs",
            AdapterPath,
        };

        Assert.Equal(expected.Order(StringComparer.Ordinal), SourcesContaining("GitHubCi"));
        foreach (var path in ProductionSources(BackendRoot()))
        {
            var relative = Path.GetRelativePath(BackendRoot(), path).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.StartsWith("DevalCopilot.Domain/", StringComparison.Ordinal)
                || relative.StartsWith("DevalCopilot.Infrastructure/Persistence/", StringComparison.Ordinal)
                || relative.StartsWith("DevalCopilot.Infrastructure/Configurations/", StringComparison.Ordinal))
            {
                var code = Code(path);
                Assert.DoesNotContain("GitHubCi", code, StringComparison.Ordinal);
                Assert.DoesNotContain("github-ci", code, StringComparison.Ordinal);
            }
        }

        Assert.DoesNotContain("GitHubCi", CodeOf("DevalCopilot.Application/Data/IDevalCopilotDbContext.cs"), StringComparison.Ordinal);
    }
}
