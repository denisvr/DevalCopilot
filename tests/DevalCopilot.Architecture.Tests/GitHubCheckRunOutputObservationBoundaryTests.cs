using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0047: the check-run output inspection is one protected, bodyless mediator query with exactly one validated query key. It
/// reads the recorded scope, binding and the GitHubCli snapshot untracked, reads the whole scope again after each of its two external
/// calls, re-admits the CLI between them, never writes, locks or opens a transaction, never touches Domain or persistence, and delegates to
/// one narrow adapter that composes one fixed read-only GraphQL invocation per phase of the vetted executable on the existing process
/// engine with raw-string variables, no mutation, shell, jq, pagination, retry or swallowed unexpected fault. Its field, identifier,
/// literal and text rules have one operation-owned policy shared by the adapter and the handler.</summary>
public sealed partial class GitHubCheckRunOutputObservationBoundaryTests
{
    private const string QueryFolder = "DevalCopilot.Application/Features/Runs/Queries/GetGitHubCheckRunOutputObservation";
    private const string Handler = QueryFolder + "/GetGitHubCheckRunOutputObservationQueryHandler.cs";
    private const string Policy = QueryFolder + "/GitHubCheckRunOutputPolicy.cs";
    private const string AdapterPath = "DevalCopilot.Infrastructure/Features/Runs/GitHubCheckRunOutputObservationAdapter.cs";
    private const string Endpoint =
        "DevalCopilot.Api/Features/Runs/GetGitHubCheckRunOutputObservation/GetGitHubCheckRunOutputObservationEndpoint.cs";
    private const string NodeIdPolicy = "DevalCopilot.Domain/Features/Projects/GitHubRepositoryNodeIdPolicy.cs";
    private const string BranchPolicy = "DevalCopilot.Application/Features/Runs/Queries/GetGitHubBranchObservation/GitHubOwnedBranchNamePolicy.cs";

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
        Assert.True(Directory.Exists(folder), "The check-run output inspection must live in its own operation folder.");
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
            "IManualTransactionCommand", "lock (", "SemaphoreSlim", "Regex", "StreamingOutputRedactor", "Trim(", "Normalize(",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_scope_and_binding_come_only_from_the_unchanged_readers_and_are_read_again_after_each_of_the_two_external_calls()
    {
        var code = CodeOf(Handler);

        Assert.Equal(3, Regex.Matches(code, @"await ReadScopeAsync\(").Count);
        Assert.Single(Regex.Matches(code, @"GitHubObservationSourceReader\.ReadAsync\("));
        Assert.Single(Regex.Matches(code, @"GitHubObservationTargetReader\.ReadAsync\("));
        Assert.Single(Regex.Matches(code, @"GitHubRepositoryIdentityBindingReader\.ReadAsync\("));
        Assert.Single(Regex.Matches(code, @"observer\.ObserveMetadataAsync\("));
        Assert.Single(Regex.Matches(code, @"observer\.ObserveOutputAsync\("));
        Assert.Contains("HostCapabilityReadinessProjector.IsStale", code, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(code, @"await ReadScopeAsync\(query\.RunId, cancellationToken\) != scope").Count);

        var firstRead = code.IndexOf("await ReadScopeAsync(", StringComparison.Ordinal);
        var usable = code.IndexOf("IsUsableTarget(snapshot", StringComparison.Ordinal);
        var cancelFirst = code.IndexOf("cancellationToken.ThrowIfCancellationRequested();", StringComparison.Ordinal);
        var metadata = code.IndexOf("observer.ObserveMetadataAsync(", StringComparison.Ordinal);
        var secondRead = code.IndexOf("!= scope", metadata, StringComparison.Ordinal);
        var firstEvaluation = code.IndexOf("Evaluate(metadata,", StringComparison.Ordinal);
        var between = code.IndexOf("IsUsableTarget(between", StringComparison.Ordinal);
        var samePath = code.IndexOf("between!.ResolvedExecutablePath", StringComparison.Ordinal);
        var clock = code.IndexOf("timeProvider.GetUtcNow() < binding.RecordedAtUtc", StringComparison.Ordinal);
        var cancelSecond = code.IndexOf("cancellationToken.ThrowIfCancellationRequested();", cancelFirst + 1, StringComparison.Ordinal);
        var output = code.IndexOf("observer.ObserveOutputAsync(", StringComparison.Ordinal);
        var thirdRead = code.IndexOf("!= scope", output, StringComparison.Ordinal);
        var secondEvaluation = code.IndexOf("Evaluate(output,", StringComparison.Ordinal);
        var stamp = code.IndexOf("var observedAt = timeProvider.GetUtcNow();", secondEvaluation, StringComparison.Ordinal);
        var observed = code.IndexOf("new GitHubCheckRunOutputObserved(", StringComparison.Ordinal);
        Assert.True(
            0 < firstRead && firstRead < usable && usable < cancelFirst && cancelFirst < metadata && metadata < secondRead
            && secondRead < firstEvaluation && firstEvaluation < between && between < samePath && samePath < clock && clock < cancelSecond
            && cancelSecond < output && output < thirdRead && thirdRead < secondEvaluation && secondEvaluation < stamp && stamp < observed);
        foreach (var forbidden in new[] { "CancellationToken.None", "GetUtcNow().UtcDateTime", "Math.Max", "DateTimeOffset.UtcNow", "after.Kind" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        Assert.Contains("observedAt < binding.RecordedAtUtc", code, StringComparison.Ordinal);
        Assert.Contains("association == first", code, StringComparison.Ordinal);
        Assert.Contains("IsAcceptedProbe(probe, checkRunId, requireDetail, target, source)", code, StringComparison.Ordinal);

        // Validation of the whole probe precedes the identity comparison, the ref comparison and the association comparison.
        var validate = code.IndexOf("IsAcceptedProbe(probe,", code.IndexOf("private static Evaluation Evaluate(", StringComparison.Ordinal), StringComparison.Ordinal);
        var identity = code.IndexOf("Outcome.IdentityChanged, null", validate, StringComparison.Ordinal);
        var refComparison = code.IndexOf("Outcome.RefNotReturned", identity, StringComparison.Ordinal);
        var commit = code.IndexOf("Outcome.CommitDifferent", refComparison, StringComparison.Ordinal);
        var association = code.IndexOf("Outcome.CheckRunUnavailable", commit, StringComparison.Ordinal);
        Assert.True(0 < validate && validate < identity && identity < refComparison && refComparison < commit && commit < association);
    }

    [Fact]
    public void The_endpoint_is_protected_bodyless_takes_one_query_value_and_refuses_a_body_and_a_bad_query_before_the_mediator()
    {
        var code = CodeOf(Endpoint);

        Assert.Contains("[HttpGet(\"{runId:guid}/github-check-run-output-observation\")]", code, StringComparison.Ordinal);
        Assert.Contains(": RunsBaseEndpoint", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromBody]", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"\[FromQuery\]"));
        Assert.DoesNotContain("[HttpPost", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnonymous", code, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DevalCopilot.Domain", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"mediator\.SendAsync\("));
        var body = code.IndexOf("HasBodyAsync(cancellationToken)", StringComparison.Ordinal);
        var query = code.IndexOf("HasExactlyTheCheckRunIdKey()", StringComparison.Ordinal);
        var send = code.IndexOf("mediator.SendAsync(", StringComparison.Ordinal);
        Assert.True(0 < body && body < query && query < send);
        Assert.Contains("GitHubCheckRunOutputPolicy.IsCheckRunId(checkRunId)", code, StringComparison.Ordinal);
        Assert.Contains("string.Equals(pair.Key, CheckRunIdKey, StringComparison.Ordinal)", code, StringComparison.Ordinal);
        Assert.Contains("private const string CheckRunIdKey = \"checkRunId\"", code, StringComparison.Ordinal);
        Assert.Contains("request.ContentLength is > 0", code, StringComparison.Ordinal);
        Assert.Contains("HeaderNames.TransferEncoding", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Trim(", code, StringComparison.Ordinal);
        Assert.Equal([Endpoint], SourcesContaining("github-check-run-output-observation\")]"));
    }

    [Fact]
    public void The_adapter_composes_one_fixed_read_only_graphql_invocation_per_phase_on_the_existing_process_engine_with_phase_specific_caps()
    {
        var code = CodeOf(AdapterPath);

        Assert.Single(Regex.Matches(code, @"_processExecutionAdapter\.ExecuteAsync\("));
        Assert.Contains("\"api\", \"graphql\", \"--hostname\", \"github.com\", \"--method\", \"POST\"", code, StringComparison.Ordinal);
        Assert.Contains(
            "\"-f\", \"query=\" + query, \"-f\", \"owner=\" + owner, \"-f\", \"name=\" + repository, \"-f\", \"ref=refs/heads/\" + branchName",
            code, StringComparison.Ordinal);
        Assert.Contains("\"-f\", \"id=\" + checkRunId", code, StringComparison.Ordinal);
        Assert.Contains("\"query($owner:String!,$name:String!,$ref:String!,$id:ID!){repository(owner:$owner,name:$name){id name nameWithOwner \"", code, StringComparison.Ordinal);
        Assert.Contains("node(id:$id){__typename ... on CheckRun{id \"", code, StringComparison.Ordinal);
        Assert.Contains("ObserveAsync(MetadataQuery, 8 * 1024, 16 * 1024, false,", code, StringComparison.Ordinal);
        Assert.Contains("ObserveAsync(OutputQuery, 256 * 1024, 512 * 1024, true,", code, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(code, @"MaxDepth = 8,").Count);
        Assert.DoesNotContain("MaxDepth = 12", code, StringComparison.Ordinal);
        Assert.Contains("_probeSlot.Wait(0, CancellationToken.None)", code, StringComparison.Ordinal);
        Assert.Contains("_probeSlot.Release()", code, StringComparison.Ordinal);
        Assert.True(code.IndexOf("finally", StringComparison.Ordinal) < code.IndexOf("_probeSlot.Release()", StringComparison.Ordinal));
        Assert.Contains("TimeSpan.FromSeconds(15)", code, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "\"-F\"", "--jq", "--paginate", "--input", "--header", "--cache", "--template", "--silent", "--verbose", "mutation", "subscription",
            "cmd", "powershell", "pwsh", "bash", "Task.Delay", "Retry", "Shell", "UseShellExecute", "File.", "Directory.Create", "Path.Combine",
            "StandardOutputSinkPath", "StandardErrorSinkPath", "StandardInput", "catch (Exception", "catch\n", "$\"", "string.Format",
            "after:", "endCursor", "checkSuites", "checkRuns", "workflowRun", "pullRequest", "first:", "hasPreviousPage", "annotations",
            "detailsUrl", "steps", "static Dictionary", "static List", "ConcurrentDictionary", "MemoryCache", "Log", "Console.",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        // The one narrow catch the decode needs: an invalid surrogate escape is reported by the decoder as an InvalidOperationException,
        // and only that single call is guarded. No other fault is swallowed.
        Assert.Single(Regex.Matches(code, @"catch \(InvalidOperationException\)"));
        Assert.Matches(
            new Regex(@"try\s*\{\s*decoded = matches\[0\]\.Value\.GetString\(\);\s*\}\s*catch \(InvalidOperationException\)"), code);
        Assert.Single(Regex.Matches(code, @"catch \(JsonException\)"));

        // Each decoded value is held to its contract, redacted once with the unchanged internal redactor (never a copied pattern, a
        // second decode or a change to shared execution) and held to the contracts again, in exactly that order.
        Assert.Single(Regex.Matches(code, @"new StreamingOutputRedactor\(\)"));
        var decode = code.IndexOf("decoded = matches[0].Value.GetString()", StringComparison.Ordinal);
        var detail = code.IndexOf("InterpretDetail(JsonProperty[] properties)", StringComparison.Ordinal);
        var validateDecoded = code.IndexOf("GitHubCheckRunOutputPolicy.IsName(names.Value)", StringComparison.Ordinal);
        var validateTrio = code.IndexOf("GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText(titles.Value", StringComparison.Ordinal);
        var redact = code.IndexOf("var name = Redact(names.Value)", StringComparison.Ordinal);
        var validateRedacted = code.IndexOf("GitHubCheckRunOutputPolicy.IsName(name) && GitHubCheckRunOutputPolicy.IsOutput(output)", StringComparison.Ordinal);
        Assert.True(
            0 < detail && detail < validateDecoded && validateDecoded < validateTrio && validateTrio < redact && redact < validateRedacted);
        Assert.True(0 < decode);
        foreach (var forbidden in new[] { "GeneratedRegex", "Regex", "[REDACTED]", "Unescape", "JsonSerializer", "Uri.", "Normalize(", "Trim(" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        // The decoded text is read through exactly one decoding call site, used only for the four text values.
        Assert.Single(Regex.Matches(code, @"\.GetString\(\);\s*\}\s*catch \(InvalidOperationException\)"));
        Assert.Equal(
            [
                "DevalCopilot.Infrastructure/Features/Runs/GitHubCheckContextsObservationAdapter.cs",
                "DevalCopilot.Infrastructure/Features/Runs/GitHubCheckRunOutputObservationAdapter.cs",
            ],
            SourcesContaining("new StreamingOutputRedactor()").Where(path => path.Contains("Runs/", StringComparison.Ordinal)));
        Assert.Contains(AdapterPath, SourcesContaining("\"graphql\""));
        Assert.Contains("ValueIsEscaped", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_output_phase_caps_exist_only_in_the_new_adapter_and_every_older_observer_keeps_its_own_limits()
    {
        Assert.Equal([AdapterPath], SourcesContaining("ObserveAsync(OutputQuery, 256 * 1024, 512 * 1024"));
        Assert.Equal([AdapterPath], SourcesContaining("ObserveAsync(MetadataQuery, 8 * 1024, 16 * 1024"));
        foreach (var adapter in new[]
        {
            "DevalCopilot.Infrastructure/Features/Runs/GitHubRepositoryIdentityAdapter.cs",
            "DevalCopilot.Infrastructure/Features/Runs/GitHubBranchObservationAdapter.cs",
            "DevalCopilot.Infrastructure/Features/Runs/GitHubCiObservationAdapter.cs",
        })
        {
            var code = CodeOf(adapter);
            Assert.Contains("MaxBytesPerStream = 8 * 1024", code, StringComparison.Ordinal);
            Assert.Contains("MaxTotalCapturedBytes = 16 * 1024", code, StringComparison.Ordinal);
            Assert.DoesNotContain("256 * 1024", code, StringComparison.Ordinal);
            Assert.DoesNotContain("GitHubCheckRunOutput", code, StringComparison.Ordinal);
        }

        var contexts = CodeOf("DevalCopilot.Infrastructure/Features/Runs/GitHubCheckContextsObservationAdapter.cs");
        Assert.Contains("MaxBytesPerStream = 128 * 1024", contexts, StringComparison.Ordinal);
        Assert.Contains("MaxTotalCapturedBytes = 256 * 1024", contexts, StringComparison.Ordinal);
        Assert.DoesNotContain("GitHubCheckRunOutput", contexts, StringComparison.Ordinal);
    }

    [Fact]
    public void The_field_rules_have_one_operation_owner_and_the_repository_id_and_branch_rules_stay_with_theirs()
    {
        Assert.Equal([NodeIdPolicy], SourcesContaining("public static class GitHubRepositoryNodeIdPolicy"));
        Assert.Equal([BranchPolicy], SourcesContaining("public static class GitHubOwnedBranchNamePolicy"));
        Assert.Equal([Policy], SourcesContaining("public static class GitHubCheckRunOutputPolicy"));
        foreach (var slice in new[] { AdapterPath, Handler })
        {
            var code = CodeOf(slice);
            Assert.Contains("GitHubRepositoryNodeIdPolicy.IsNodeId(", code, StringComparison.Ordinal);
            Assert.Contains("GitHubRepositoryNamePolicy.IsOwner(", code, StringComparison.Ordinal);
            Assert.Contains("GitHubRepositoryNamePolicy.IsRepository(", code, StringComparison.Ordinal);
            Assert.Contains("GitHubCheckRunOutputPolicy.IsNode(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("IsAsciiAlphanumeric", code, StringComparison.Ordinal);
            Assert.DoesNotContain("GitHubCheckContextsPolicy", code, StringComparison.Ordinal);
        }

        Assert.Contains("GitHubOwnedBranchNamePolicy.IsOwnedBranch(", CodeOf(AdapterPath), StringComparison.Ordinal);
        Assert.Contains("GitHubOwnedBranchNamePolicy.IsOwnedBranch(", CodeOf(Handler), StringComparison.Ordinal);
        Assert.Contains("GitHubObservationTarget.IsCommitSha(", CodeOf(AdapterPath), StringComparison.Ordinal);
        Assert.Contains("GitHubObservationTarget.IsCommitSha(", CodeOf(Handler), StringComparison.Ordinal);

        // The literal enumerations, the output text contract and its bounds exist in the policy, and the Unicode tables only there.
        var policy = CodeOf(Policy);
        Assert.Contains("UnicodeCategory.Format", policy, StringComparison.Ordinal);
        Assert.Contains("rune.Value is 0x09 or 0x0A or 0x0D", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("UnicodeCategory", CodeOf(AdapterPath), StringComparison.Ordinal);
        Assert.DoesNotContain("UnicodeCategory", CodeOf(Handler), StringComparison.Ordinal);
        Assert.Contains("MaxTitleBytes = 256", policy, StringComparison.Ordinal);
        Assert.Contains("MaxSummaryBytes = 8 * 1024", policy, StringComparison.Ordinal);
        Assert.Contains("MaxTextBytes = 16 * 1024", policy, StringComparison.Ordinal);
        Assert.Contains("MaxCombinedOutputBytes = 24 * 1024", policy, StringComparison.Ordinal);
        Assert.Contains("\"ACTION_REQUIRED\" or \"CANCELLED\" or \"FAILURE\" or \"NEUTRAL\" or \"SKIPPED\" or \"STALE\" or \"STARTUP_FAILURE\" or \"SUCCESS\"", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("GitHubCheckContexts", policy, StringComparison.Ordinal);
        foreach (var translated in new[] { "Passed", "Approved", "Ready", "Green", "Required", "Diagnos" })
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
            "DevalCopilot.Api/Features/Runs/GetGitHubCheckRunOutputObservation/GetGitHubCheckRunOutputObservationEndpoint.cs",
            "DevalCopilot.Api/Features/Runs/GetGitHubCheckRunOutputObservation/GetGitHubCheckRunOutputObservationResponse.cs",
            "DevalCopilot.Api/Features/Runs/GetGitHubCheckRunOutputObservation/GitHubCheckRunOutputCheckRunResponse.cs",
            "DevalCopilot.Api/Features/Runs/GetGitHubCheckRunOutputObservation/GitHubCheckRunOutputObservationResponse.cs",
            "DevalCopilot.Api/Features/Runs/GetGitHubCheckRunOutputObservation/GitHubCheckRunOutputRefResponse.cs",
            "DevalCopilot.Api/Features/Runs/GetGitHubCheckRunOutputObservation/GitHubCheckRunOutputTextResponse.cs",
            "DevalCopilot.Api/Program.cs",
            "DevalCopilot.Application/Features/Runs/Errors/GitHubCheckRunOutputObservationErrors.cs",
            QueryFolder + "/GetGitHubCheckRunOutputObservationQuery.cs",
            QueryFolder + "/GetGitHubCheckRunOutputObservationQueryHandler.cs",
            QueryFolder + "/GetGitHubCheckRunOutputObservationQueryResult.cs",
            QueryFolder + "/GitHubCheckRunAssociation.cs",
            QueryFolder + "/GitHubCheckRunDetail.cs",
            QueryFolder + "/GitHubCheckRunNode.cs",
            QueryFolder + "/GitHubCheckRunOutputCheckRun.cs",
            QueryFolder + "/GitHubCheckRunOutputObservationStatus.cs",
            QueryFolder + "/GitHubCheckRunOutputObserved.cs",
            Policy,
            QueryFolder + "/GitHubCheckRunOutputRef.cs",
            QueryFolder + "/GitHubCheckRunOutputText.cs",
            QueryFolder + "/GitHubCheckRunProbe.cs",
            QueryFolder + "/IGitHubCheckRunOutputObserver.cs",
            AdapterPath,
        };

        Assert.Equal(expected.Order(StringComparer.Ordinal), SourcesContaining("GitHubCheckRunOutput"));
        foreach (var path in ProductionSources(BackendRoot()))
        {
            var relative = Path.GetRelativePath(BackendRoot(), path).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.StartsWith("DevalCopilot.Domain/", StringComparison.Ordinal)
                || relative.StartsWith("DevalCopilot.Infrastructure/Persistence/", StringComparison.Ordinal)
                || relative.StartsWith("DevalCopilot.Infrastructure/Configurations/", StringComparison.Ordinal))
            {
                var code = Code(path);
                Assert.DoesNotContain("GitHubCheckRunOutput", code, StringComparison.Ordinal);
                Assert.DoesNotContain("github-check-run-output", code, StringComparison.Ordinal);
            }
        }

        Assert.DoesNotContain("GitHubCheckRunOutput", CodeOf("DevalCopilot.Application/Data/IDevalCopilotDbContext.cs"), StringComparison.Ordinal);

        // The earlier operations are unchanged by this one: none of them mentions it.
        foreach (var earlier in new[]
        {
            "DevalCopilot.Application/Features/Runs/Queries/GetGitHubCheckContextsObservation",
            "DevalCopilot.Application/Features/Runs/Queries/GetGitHubCiObservation",
            "DevalCopilot.Application/Features/Runs/Queries/GetGitHubBranchObservation",
            "DevalCopilot.Application/Features/Runs/Policies/GitHubObservation",
        })
        {
            var folder = Path.Combine(BackendRoot(), earlier.Replace('/', Path.DirectorySeparatorChar));
            foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                Assert.DoesNotContain("GitHubCheckRunOutput", Code(file), StringComparison.Ordinal);
            }
        }
    }
}
