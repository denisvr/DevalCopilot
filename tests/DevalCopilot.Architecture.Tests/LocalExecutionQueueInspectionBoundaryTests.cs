using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0064: the outstanding-admission overview is one read-only mediator query over one bounded, untracked, provider-neutral
/// statement that reuses the ADR-0063 whole-ledger proof. It never invokes an admission, ownership, eligibility, dispatch, recorder or
/// mutation handler, reads no latest or current row, Git, filesystem, process, provider, account or artifact state, selects no project
/// path, settings or digest, and writes nothing. Its one endpoint is an authorized bodyless and queryless MVC GET that dispatches
/// through the mediator and answers with an API-owned graph.</summary>
public sealed partial class LocalExecutionQueueInspectionBoundaryTests
{
    private const string ApplicationFolder = "DevalCopilot.Application/Features/Runs/Queries/GetLocalExecutionQueueInspection";
    private const string ApiFolder = "DevalCopilot.Api/Features/LocalExecutionQueue";

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

    private static string[] Sources(string folder)
    {
        var path = Path.Combine(BackendRoot(), folder.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(path), $"{folder} must exist as its own operation folder.");
        return Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
    }

    private static string ApplicationCode() => string.Join('\n', Sources(ApplicationFolder).Select(Code));

    private static string ApiCode() => string.Join('\n', Sources(ApiFolder).Select(Code));

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
                "CollaborationMessages", "GitCheckpoints", "GitWorkspaces", "LocalExecutionAdmissions", "LocalExecutionSequenceRecipes",
                "LocalExecutionSequenceSteps", "LocalExecutionSequences", "Projects", "Runs",
            ],
            DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in new[]
        {
            "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "BeginTransaction", "IsModified", "AsTracking", "TimeProvider",
            "IRunEventNotifier", "RunEvent.Record", "IApplicationMediator", "IGit", "ILocalCommit", "IProcess", "ProcessStart", "IAgent",
            "IAttemptDurabilityProbe", "ILocalExecutionStepProbe", "IArtifactStore", "File.", "Directory.", "Path.Combine", "Path.GetFull",
            "HttpClient", "Task.Delay", "Include(", "ThenInclude(", "FromSql", "ExecuteSql", "Environment.", "Process.", "ToListAsync",
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
            "LocalExecutionCandidateReader", "LocalExecutionQueueEvaluator", "LocalExecutionPlanEligibility", "LocalExecutionRefusalProof",
            "LocalExecutionFinalEvidence", "WorkspaceExecution", "ExecutionCapacity", "RepositoryMutationLeases", "VerificationCommands",
            "Commands.", "CommandHandler", "OrderByDescending", "MaxAsync", "MinAsync", "LastOrDefault", "Latest", "Current",
            "PreparationOwnerToken", "SettingsSnapshot", "ConfirmationSha256", "PlanInputsSha256", "WorkspacePath", "CanonicalPath",
            "RegistrationIdentityKey", "ArgumentsJson", "ExecutablePath",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Membership_starts_at_the_admissions_one_statement_bounds_every_read_and_the_proof_is_the_unchanged_ledger_proof()
    {
        var statement = Code(Path.Combine(BackendRoot(), ApplicationFolder, "LocalExecutionQueueInspectionStatement.cs"));
        var projection = Code(Path.Combine(BackendRoot(), ApplicationFolder, "LocalExecutionQueueInspectionProjection.cs"));
        var proof = Code(Path.Combine(BackendRoot(), ApplicationFolder, "LocalExecutionQueueInspectionProof.cs"));
        var handler = Code(Path.Combine(BackendRoot(), ApplicationFolder, "GetLocalExecutionQueueInspectionQueryHandler.cs"));

        // Candidates come from the admissions, bounded to 64 + 4 + 1, with only a recognized Consumed or Retired state excluded.
        Assert.Contains("from candidate in candidates", statement, StringComparison.Ordinal);
        Assert.Contains(".Take(CandidateBound)", statement, StringComparison.Ordinal);
        Assert.Contains("LocalExecutionSequencePolicy.MaximumQueuedCycles + LocalExecutionSequencePolicy.MaximumHeldPreparations + 1", statement, StringComparison.Ordinal);
        Assert.Contains("RowCeiling = CandidateBound * LocalExecutionAdmissionHistoryStatement.RowBound + 1", statement, StringComparison.Ordinal);
        Assert.Contains(".Take(RowCeiling)", statement, StringComparison.Ordinal);
        Assert.Contains(".Take(LocalExecutionAdmissionHistoryStatement.AdmissionBound)", statement, StringComparison.Ordinal);
        Assert.Contains(".Take(2)", statement, StringComparison.Ordinal);
        Assert.Contains("LocalExecutionAdmissionState.Consumed ? 3", projection, StringComparison.Ordinal);
        Assert.Contains("LocalExecutionAdmissionState.Retired ? 4", projection, StringComparison.Ordinal);
        Assert.Contains("ProjectNameByteBound = 800", projection, StringComparison.Ordinal);
        Assert.Contains("LocalExecutionAdmissionHistoryProjection.RowOf(dbContext)", projection, StringComparison.Ordinal);

        // One await of one statement, then the pure proof; the whole-ledger proof of ADR-0063 is reused, never copied.
        Assert.Single(Regex.Matches(handler, @"\.ToArrayAsync\("));
        Assert.Single(Regex.Matches(handler, @"LocalExecutionQueueInspectionStatement\.For\(dbContext\)"));
        Assert.Contains("LocalExecutionQueueInspectionProof.Prove(rows)", handler, StringComparison.Ordinal);
        Assert.Contains("LocalExecutionAdmissionHistoryProof.Prove(", proof, StringComparison.Ordinal);
        Assert.DoesNotContain("IsLowercaseSha256", proof, StringComparison.Ordinal);
        Assert.Contains("LocalExecutionSequencePolicy.MaximumQueuedCycles", proof, StringComparison.Ordinal);
        Assert.Contains("LocalExecutionSequencePolicy.MaximumHeldPreparations", proof, StringComparison.Ordinal);
    }

    [Fact]
    public void The_one_endpoint_dispatches_only_through_the_mediator_is_an_authorized_bodyless_get_and_answers_with_api_owned_graphs()
    {
        var endpoints = Sources(ApiFolder).Where(path => path.EndsWith("Endpoint.cs", StringComparison.Ordinal)).ToArray();
        var concrete = Assert.Single(endpoints, path => path.EndsWith("GetLocalExecutionQueueInspectionEndpoint.cs", StringComparison.Ordinal));
        var source = Code(concrete);
        var baseSource = Code(Assert.Single(endpoints, path => path.EndsWith("LocalExecutionQueueBaseEndpoint.cs", StringComparison.Ordinal)));
        var code = ApiCode();

        Assert.Equal(2, endpoints.Length);
        Assert.Contains(": LocalExecutionQueueBaseEndpoint", source, StringComparison.Ordinal);
        Assert.Contains("[Authorize]", baseSource, StringComparison.Ordinal);
        Assert.Contains("[Route(\"api/local-execution-queue\")]", baseSource, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"\[HttpGet\("));
        Assert.Contains("[HttpGet(\"inspection\")]", source, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"mediator\.SendAsync\("));
        Assert.Contains("[TypeFilter(typeof(LocalExecutionInspectionBodylessGuardFilter))]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromBody]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromQuery]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[AllowAnonymous]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeProvider", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IArtifactStore", code, StringComparison.Ordinal);
        foreach (var path in Sources(ApiFolder).Where(path => path.EndsWith("Response.cs", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("DevalCopilot.Application", Code(path), StringComparison.Ordinal);
            Assert.DoesNotContain("DevalCopilot.Domain", Code(path), StringComparison.Ordinal);
        }
    }
}
