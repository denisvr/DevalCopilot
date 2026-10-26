using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0069: the recorded project verification inspection is three read-only mediator queries over bounded untracked scalar
/// projections and operation-owned readers. They never invoke the live project verification list, the output viewer, the claim,
/// dispatch, recorder, eligibility, ownership or Agent/sequence membership policies, read no current recipe, run, attempt, latest or
/// current row, Git, filesystem, process or provider state, and only the window reaches the artifact store, through the sealed
/// verification alone. The three endpoints are authorized bodyless MVC GETs that dispatch through the mediator and answer with API-owned
/// graphs behind a guard filter that runs before binding.</summary>
public sealed partial class ProjectVerificationInspectionBoundaryTests
{
    private static readonly string[] ApplicationFolders =
    [
        "DevalCopilot.Application/Features/Projects/Queries/GetProjectVerificationHistory",
        "DevalCopilot.Application/Features/Projects/Queries/GetProjectVerificationInspection",
        "DevalCopilot.Application/Features/Projects/Queries/GetProjectVerificationOutputWindow",
        "DevalCopilot.Application/Features/Projects/Policies/ProjectVerificationInspection",
    ];

    private static readonly string[] ApiFolders =
    [
        "DevalCopilot.Api/Features/Projects/GetProjectVerificationHistory",
        "DevalCopilot.Api/Features/Projects/GetProjectVerificationInspection",
        "DevalCopilot.Api/Features/Projects/GetProjectVerificationOutputWindow",
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

    private static string FileCode(string folder, string file) =>
        Code(Path.Combine(BackendRoot(), folder.Replace('/', Path.DirectorySeparatorChar), file));

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
            ["GitCheckpoints", "GitWorkspaces", "Projects", "VerificationExecutions", "VerificationOutputArtifacts"],
            DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in new[]
        {
            "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "BeginTransaction", "IsModified", "AsTracking", "TimeProvider",
            "IRunEventNotifier", "RunEvent.Record", "IApplicationMediator", "IGit", "ILocalCommit", "IProcess", "ProcessStart", "IAgent",
            "File.", "Directory.", "Path.", "HttpClient", "Task.Delay", "Include(", "ThenInclude(", "FromSql", "ExecuteSql", "Environment.",
            "Process.",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_live_list_viewer_claim_dispatch_eligibility_ownership_or_membership_policy_is_consulted_and_nothing_reads_a_latest_row()
    {
        var code = ApplicationCode();

        foreach (var forbidden in new[]
        {
            "GetProjectVerificationExecutions", "GetVerificationExecutionOutput", "ClaimVerificationExecution", "MarkVerificationExecutionDispatched",
            "RecordVerificationExecution", "VerificationDispatch", "EligibleVerificationExecutions", "WorkspaceExecution", "ExecutionCapacity",
            "RepositoryMutationLeases", "dbContext.LocalExecution", "LocalExecutionOwnership", "LocalExecutionInspection",
            "LocalExecutionSequencePolicy", "RecordedVerificationMembership", "AgentAttemptVerificationErrors",
            "ScalarAgentAttemptIdentity", "AttemptVerificationEvidence", "VerificationCommands", "dbContext.Runs", "dbContext.Attempts",
            "CommandHandler", "Commands.", "MaxAsync", "MinAsync", "LastOrDefault", "Latest", "Current", "ExecutablePath", "WorkspacePath",
            "Arguments", "TimeoutSeconds", "ReserveExecutionNumber",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        // The newest-first keyset page is the only ordered read, and it never asks for a maximum or an unbounded list.
        Assert.Single(Regex.Matches(code, "OrderByDescending"));
        Assert.Equal(
            "ProjectVerificationHistoryReader.cs",
            Path.GetFileName(Sources(ApplicationFolders).Single(path => Code(path).Contains("OrderByDescending", StringComparison.Ordinal))));
    }

    [Fact]
    public void No_fault_is_swallowed_and_every_scalar_read_is_a_bounded_projection_never_a_whole_entity_or_an_unbounded_list()
    {
        var code = ApplicationCode();

        Assert.DoesNotContain("catch", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ToListAsync", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"dbContext\.[A-Za-z]+\.AsNoTracking\(\)\s*\.ToArrayAsync", code);
        Assert.Contains("PageSize = 20", code, StringComparison.Ordinal);
        Assert.Contains(".Take(ProjectVerificationInspectionProjection.PageSize + 1)", code, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(code, @"\.Take\(2\)").Count);
        Assert.Contains("MaximumSafeInteger = (1L << 53) - 1", code, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "Summary", "DetailCode", "Snapshot", "Digest" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        // Every identifier and time is projected as bounded raw bytes and admitted by the one storage parser: no row record can hold a
        // typed Guid or DateTimeOffset, so no projection can materialize one.
        foreach (var rowFile in Directory.EnumerateFiles(
            Path.Combine(BackendRoot(), "DevalCopilot.Application", "Features", "Projects", "Policies", "ProjectVerificationInspection"), "*Row.cs"))
        {
            var row = Code(rowFile);
            Assert.DoesNotContain("Guid ", row, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTimeOffset", row, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Membership_is_decided_by_the_database_before_any_selected_fact_is_interpreted_and_the_ownership_facts_are_one_statement()
    {
        var reader = FileCode("DevalCopilot.Application/Features/Projects/Policies/ProjectVerificationInspection", "ProjectVerificationInspectionReader.cs");

        var member = reader.IndexOf(".AnyAsync(", StringComparison.Ordinal);
        var statement = reader.IndexOf(".ProjectExecution(", StringComparison.Ordinal);
        var prove = reader.IndexOf("Prove(rows[0]", StringComparison.Ordinal);
        Assert.True(member >= 0 && member < statement && statement < prove);
        Assert.Single(Regex.Matches(reader, @"\.ProjectExecution\("));
        Assert.Equal(6, Regex.Matches(reader, @"dbContext\.[A-Za-z]+\.AsNoTracking\(\)").Count);
        var projection = FileCode(
            "DevalCopilot.Application/Features/Projects/Policies/ProjectVerificationInspection", "ProjectVerificationInspectionProjection.cs");
        Assert.Equal(3, Regex.Matches(projection, @"\.Join\(").Count);
        Assert.Equal(2, Regex.Matches(reader, @"dbContext\.Projects\.AsNoTracking\(\)").Count);
    }

    [Fact]
    public void The_window_proves_the_execution_before_the_stream_and_the_stream_before_the_store_and_uses_only_the_sealed_verification()
    {
        var folder = "DevalCopilot.Application/Features/Projects/Queries/GetProjectVerificationOutputWindow";
        var handler = FileCode(folder, "GetProjectVerificationOutputWindowQueryHandler.cs");

        var bounds = handler.IndexOf("query.FromOffset is < 0", StringComparison.Ordinal);
        var cap = handler.IndexOf("query.MaxBytes is <", StringComparison.Ordinal);
        var stream = handler.IndexOf("query.Stream is not", StringComparison.Ordinal);
        var execution = handler.IndexOf("ProjectVerificationInspectionReader.ReadAsync(", StringComparison.Ordinal);
        var storage = handler.IndexOf("ProjectVerificationOutputReader.ReadStorageAsync(", StringComparison.Ordinal);
        var admissible = handler.IndexOf("IsAdmissibleStorage(", StringComparison.Ordinal);
        var beyond = handler.IndexOf("OffsetBeyondLength()", StringComparison.Ordinal);
        var store = handler.IndexOf("artifactStore.VerifyAndReadSealedAsync(", StringComparison.Ordinal);
        Assert.True(0 <= bounds && bounds < cap && cap < stream && stream < execution && execution < storage);
        Assert.True(storage < admissible && admissible < beyond && beyond < store);
        Assert.Single(Regex.Matches(handler, @"artifactStore\."));
        Assert.Contains("RecordedVerificationOutputAdmission.IsAdmissibleStorage", handler, StringComparison.Ordinal);

        // Only the window handler may see the artifact store, and no other member of the port is ever used.
        foreach (var path in Sources(ApplicationFolders).Where(path => !path.EndsWith("GetProjectVerificationOutputWindowQueryHandler.cs", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("IArtifactStore", Code(path), StringComparison.Ordinal);
        }

        Assert.DoesNotContain("SealedReadStatus.Partial", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void The_metadata_and_history_handlers_take_only_the_database_so_they_can_open_no_artifact_file()
    {
        foreach (var (folder, file) in new[]
        {
            ("DevalCopilot.Application/Features/Projects/Queries/GetProjectVerificationHistory", "GetProjectVerificationHistoryQueryHandler.cs"),
            ("DevalCopilot.Application/Features/Projects/Queries/GetProjectVerificationInspection", "GetProjectVerificationInspectionQueryHandler.cs"),
        })
        {
            var handler = FileCode(folder, file);
            Assert.Contains("(IDevalCopilotDbContext dbContext)", handler, StringComparison.Ordinal);
            Assert.DoesNotContain("IArtifactStore", handler, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_three_endpoints_dispatch_only_through_the_mediator_are_bodyless_gets_and_answer_with_api_owned_graphs()
    {
        var code = ApiCode();
        var endpoints = Sources(ApiFolders).Where(path => path.EndsWith("Endpoint.cs", StringComparison.Ordinal)).ToArray();

        Assert.Equal(3, endpoints.Length);
        var guards = new Dictionary<string, string>
        {
            ["GetProjectVerificationHistoryEndpoint.cs"] = "ProjectVerificationHistoryGuardFilter",
            ["GetProjectVerificationInspectionEndpoint.cs"] = "ProjectVerificationDetailGuardFilter",
            ["GetProjectVerificationOutputWindowEndpoint.cs"] = "ProjectVerificationWindowGuardFilter",
        };
        foreach (var path in endpoints)
        {
            var source = Code(path);
            Assert.Contains(": ProjectsBaseEndpoint", source, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(source, @"\[HttpGet\("));
            Assert.Single(Regex.Matches(source, @"mediator\.SendAsync\("));
            Assert.Contains($"[TypeFilter(typeof({guards[Path.GetFileName(path)]}))]", source, StringComparison.Ordinal);
            Assert.DoesNotContain("[FromBody]", source, StringComparison.Ordinal);
            Assert.DoesNotContain("[AllowAnonymous]", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("IArtifactStore", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeProvider", code, StringComparison.Ordinal);
        foreach (var path in Sources(ApiFolders).Where(path => path.EndsWith("Response.cs", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("DevalCopilot.Application", Code(path), StringComparison.Ordinal);
            Assert.DoesNotContain("DevalCopilot.Domain", Code(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_guard_runs_before_binding_and_admits_only_exact_ordinal_queries_and_no_body()
    {
        var guard = FileCode(
            "DevalCopilot.Api/Features/Projects/GetProjectVerificationHistory", "ProjectVerificationInspectionGuardFilter.cs");

        Assert.Contains(": IAsyncResourceFilter", guard, StringComparison.Ordinal);
        Assert.Contains("allowedKeys.Contains(pair.Key, StringComparer.Ordinal)", guard, StringComparison.Ordinal);
        Assert.Contains("pair.Value.Count != 1", guard, StringComparison.Ordinal);
        Assert.Contains("request.ContentLength is > 0", guard, StringComparison.Ordinal);
        Assert.Contains("HeaderNames.TransferEncoding", guard, StringComparison.Ordinal);
        Assert.Contains("request.Body.ReadAsync(new byte[1]", guard, StringComparison.Ordinal);
        Assert.Contains("ProjectVerificationInspectionErrors.QueryNotAllowed()", guard, StringComparison.Ordinal);
        Assert.Contains("ProjectVerificationInspectionErrors.BodyNotAllowed()", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("mediator", guard, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            guard.IndexOf("IsAcceptedQuery(request)", StringComparison.Ordinal) < guard.IndexOf("request.Body.ReadAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void The_shared_agent_and_sequence_inspection_handlers_and_the_live_project_endpoints_are_not_touched_by_the_new_operations()
    {
        foreach (var path in new[]
        {
            "DevalCopilot.Application/Features/Runs/Queries/GetAgentAttemptVerificationEvidence",
            "DevalCopilot.Application/Features/Runs/Queries/GetAgentAttemptVerificationOutputWindow",
            "DevalCopilot.Application/Features/Runs/Policies/AgentAttemptVerification",
        })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(BackendRoot(), path.Replace('/', Path.DirectorySeparatorChar)), "*.cs"))
            {
                Assert.DoesNotContain("ProjectVerificationInspection", Code(file), StringComparison.Ordinal);
                Assert.DoesNotContain("ProjectVerificationHistory", Code(file), StringComparison.Ordinal);
            }
        }
    }
}
