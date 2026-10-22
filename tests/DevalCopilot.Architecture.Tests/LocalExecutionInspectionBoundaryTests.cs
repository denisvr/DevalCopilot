using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0061: the recorded local execution inspection is three read-only mediator queries over bounded untracked scalar
/// projections and one shared structural reader. They never invoke an admission, ownership, eligibility, dispatch, recorder or mutation
/// handler, read no latest or current row, Git, filesystem, process, provider or account state, and reach artifact bytes only through the
/// artifact store's sealed verification in the window handler, after the association was re-proved. The three endpoints are authorized
/// bodyless MVC GETs that dispatch through the mediator and answer with API-owned graphs.</summary>
public sealed partial class LocalExecutionInspectionBoundaryTests
{
    private static readonly string[] ApplicationFolders =
    [
        "DevalCopilot.Application/Features/Runs/Queries/GetLocalExecutionSequenceInspection",
        "DevalCopilot.Application/Features/Runs/Queries/GetLocalExecutionStepInspection",
        "DevalCopilot.Application/Features/Runs/Queries/GetLocalExecutionVerificationOutputWindow",
        "DevalCopilot.Application/Features/Runs/Policies/LocalExecutionInspection",
    ];

    private static readonly string[] ApiFolders =
    [
        "DevalCopilot.Api/Features/Runs/GetLocalExecutionSequenceInspection",
        "DevalCopilot.Api/Features/Runs/GetLocalExecutionStepInspection",
        "DevalCopilot.Api/Features/Runs/GetLocalExecutionVerificationOutputWindow",
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

    [GeneratedRegex(@"dbContext\.([A-Za-z]+)")]
    private static partial Regex DbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.AsNoTracking\(\)")]
    private static partial Regex UntrackedDbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.(Add|AddRange|Remove|RemoveRange|Update|UpdateRange|Attach)\(")]
    private static partial Regex DbSetWrite();

    [Fact]
    public void The_application_slice_only_reads_untracked_rows_of_the_recorded_tables_and_writes_nothing()
    {
        var code = ApplicationCode();

        Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
        Assert.DoesNotMatch(DbSetWrite(), code);
        Assert.Equal(
            [
                "Attempts", "CollaborationMessages", "GitCheckpoints", "GitWorkspaces", "LocalExecutionSequenceRecipes",
                "LocalExecutionSequenceSteps", "LocalExecutionSequences", "Runs", "VerificationExecutions", "VerificationOutputArtifacts",
            ],
            DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in new[]
        {
            "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "BeginTransaction", "IsModified", "AsTracking", "TimeProvider",
            "IRunEventNotifier", "RunEvent.Record", "IApplicationMediator", "IGit", "ILocalCommit", "IProcess", "ProcessStart", "IAgent",
            "IAttemptDurabilityProbe", "ILocalExecutionStepProbe", "File.", "Directory.", "Path.Combine", "Path.GetFull", "HttpClient",
            "Task.Delay", "Include(", "ThenInclude(", "FromSql", "ExecuteSql", "Environment.", "Process.",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_admission_ownership_eligibility_fence_dispatch_or_recorder_policy_is_consulted_and_nothing_reads_a_latest_row()
    {
        var code = ApplicationCode();

        foreach (var forbidden in new[]
        {
            "LocalExecutionOwnership", "LocalExecutionSequenceFence", "LocalExecutionStepAuthority", "LocalExecutionDispatchGate",
            "LocalExecutionCandidateReader", "LocalExecutionPlanEligibility", "LocalExecutionRefusalProof", "LocalExecutionExitProof",
            "LocalExecutionFinalEvidence", "WorkspaceExecution", "ExecutionCapacity", "RepositoryMutationLeases", "VerificationCommands",
            "Commands.", "CommandHandler", "OrderByDescending", "MaxAsync", "MinAsync", "LastOrDefault", "Latest", "Current",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Only_the_window_handler_reaches_the_artifact_store_through_one_sealed_verification_after_the_association_is_proved()
    {
        var sources = Sources(ApplicationFolders);
        var users = sources.Where(path => Code(path).Contains("IArtifactStore", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["GetLocalExecutionVerificationOutputWindowQueryHandler.cs"], users.Select(Path.GetFileName));
        var code = Code(users[0]);

        Assert.Single(Regex.Matches(code, @"artifactStore\.[A-Za-z]+\("));
        Assert.Single(Regex.Matches(code, @"artifactStore\.VerifyAndReadSealedAsync\("));
        var proof = code.IndexOf("LocalExecutionOperationProof.ProveVerificationAsync(", StringComparison.Ordinal);
        var read = code.IndexOf("artifactStore.VerifyAndReadSealedAsync(", StringComparison.Ordinal);
        var structure = code.IndexOf("LocalExecutionInspectionReader.ReadAsync(", StringComparison.Ordinal);
        Assert.True(structure >= 0 && structure < proof && proof < read, "Structure, then the association proof, then the one sealed read.");
        Assert.Contains("RecordedVerificationOutputAdmission.Admit(", code, StringComparison.Ordinal);
        Assert.Contains("RecordedVerificationOutputAdmission.IsAdmissibleStorage(", code, StringComparison.Ordinal);
        Assert.Contains("output.VerificationExecutionId == executionId && output.Purpose == purpose", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_only_handled_exceptions_are_the_expected_parse_failures_of_the_selected_operations_own_arguments()
    {
        var code = ApplicationCode();

        Assert.DoesNotContain("catch (Exception", code, StringComparison.Ordinal);
        Assert.DoesNotContain("catch\n", code, StringComparison.Ordinal);
        Assert.Equal(
            ["DecoderFallbackException", "JsonException"],
            Regex.Matches(code, @"catch \((\w+)\)").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_scalar_read_is_a_bounded_projection_never_a_whole_entity_or_an_unbounded_list()
    {
        var code = ApplicationCode();

        Assert.DoesNotContain("ToListAsync<", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"dbContext\.[A-Za-z]+\.AsNoTracking\(\)\s*\.ToListAsync", code);
        Assert.Contains("StepBound = 3 * LocalExecutionSequencePolicy.MaximumRecipes + 6 + 1", code, StringComparison.Ordinal);
        Assert.Contains("RecipeBound = LocalExecutionSequencePolicy.MaximumRecipes + 1", code, StringComparison.Ordinal);
        Assert.Contains("ArgumentsByteBound = 2 * 1024 * 1024", code, StringComparison.Ordinal);
        Assert.Contains("ExecutablePathByteBound = 128 * 1024", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PreparationOwnerToken", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsSnapshot", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmationSha256", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PlanInputsSha256", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspacePath", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_three_endpoints_dispatch_only_through_the_mediator_are_bodyless_gets_and_answer_with_api_owned_graphs()
    {
        var code = ApiCode();
        var endpoints = Sources(ApiFolders).Where(path => path.EndsWith("Endpoint.cs", StringComparison.Ordinal)).ToArray();

        Assert.Equal(3, endpoints.Length);
        foreach (var path in endpoints)
        {
            var source = Code(path);
            Assert.Contains(": RunsBaseEndpoint", source, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(source, @"\[HttpGet\("));
            Assert.Single(Regex.Matches(source, @"mediator\.SendAsync\("));
            Assert.Contains("[TypeFilter(typeof(LocalExecutionInspection", source, StringComparison.Ordinal);
            Assert.DoesNotContain("[FromBody]", source, StringComparison.Ordinal);
            Assert.DoesNotContain("[AllowAnonymous]", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("IArtifactStore", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeProvider", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"record \w+\([^)]*\b(LocalExecutionInspection(Step|Header)|LocalExecutionVerificationFacts)\b[^)]*\)", code);
        foreach (var path in Sources(ApiFolders).Where(path => path.EndsWith("Response.cs", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("DevalCopilot.Application", Code(path), StringComparison.Ordinal);
            Assert.DoesNotContain("DevalCopilot.Domain", Code(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_body_and_query_guards_run_before_binding_and_admit_only_the_documented_shapes()
    {
        var bodyless = Code(Path.Combine(BackendRoot(), "DevalCopilot.Api", "Features", "Runs", "GetLocalExecutionSequenceInspection", "LocalExecutionInspectionBodylessGuardFilter.cs"));
        var window = Code(Path.Combine(BackendRoot(), "DevalCopilot.Api", "Features", "Runs", "GetLocalExecutionVerificationOutputWindow", "LocalExecutionInspectionWindowGuardFilter.cs"));
        var body = Code(Path.Combine(BackendRoot(), "DevalCopilot.Api", "Features", "Runs", "GetLocalExecutionSequenceInspection", "LocalExecutionInspectionBodyGuard.cs"));

        Assert.Contains(": IAsyncResourceFilter", bodyless, StringComparison.Ordinal);
        Assert.Contains(": IAsyncResourceFilter", window, StringComparison.Ordinal);
        Assert.Contains("request.Query.Count > 0", bodyless, StringComparison.Ordinal);
        Assert.Contains("pair.Key is not (\"fromOffset\" or \"maxBytes\") || pair.Value.Count != 1", window, StringComparison.Ordinal);
        Assert.Contains("ContentLength is > 0", body, StringComparison.Ordinal);
        Assert.Contains("HeaderNames.TransferEncoding", body, StringComparison.Ordinal);
        Assert.Contains("request.Body.ReadAsync(new byte[1]", body, StringComparison.Ordinal);
        foreach (var source in new[] { bodyless, window })
        {
            Assert.Contains("LocalExecutionInspectionErrors.QueryNotAllowed()", source, StringComparison.Ordinal);
            Assert.Contains("LocalExecutionInspectionErrors.BodyNotAllowed()", source, StringComparison.Ordinal);
            Assert.DoesNotContain("mediator", source, StringComparison.OrdinalIgnoreCase);
        }
    }
}
