using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0071: the recorded abandonment inspection is one read-only mediator query over one bounded, untracked scalar statement
/// over the run, its registered project and its exact run-keyed run.abandoned events. It reuses the unchanged shared reason
/// interpretation and coherence policy instead of repeating them, never invokes the live abandonment status, command, intake or
/// replay readers, reads no objective, stage, budget, attempt, workspace, lease, commit or artifact and swallows no database fault.
/// The endpoint is an authorized bodyless, queryless MVC GET that dispatches through the mediator and answers with an API-owned
/// graph.</summary>
public sealed partial class RunAbandonmentInspectionBoundaryTests
{
    private static readonly string[] ApplicationFolders =
    [
        "DevalCopilot.Application/Features/Runs/Queries/GetRunAbandonmentInspection",
        "DevalCopilot.Application/Features/Runs/Policies/RecordedRunAbandonment",
    ];

    private static readonly string[] ApiFolders = ["DevalCopilot.Api/Features/Runs/GetRunAbandonmentInspection"];

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

    private static string PolicyFile(string name) => Code(Path.Combine(
        BackendRoot(), "DevalCopilot.Application", "Features", "Runs", "Policies", "RecordedRunAbandonment", name));

    [GeneratedRegex(@"dbContext\.([A-Za-z]+)")]
    private static partial Regex DbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.AsNoTracking\(\)")]
    private static partial Regex UntrackedDbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.(Add|AddRange|Remove|RemoveRange|Update|UpdateRange|Attach)\(")]
    private static partial Regex DbSetWrite();

    [Fact]
    public void The_application_slice_only_reads_untracked_rows_of_the_run_project_and_event_tables_and_writes_nothing()
    {
        var code = ApplicationCode();

        Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
        Assert.DoesNotMatch(DbSetWrite(), code);
        Assert.Equal(
            ["Events", "Projects", "Runs"],
            DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in new[]
        {
            "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "BeginTransaction", "IsModified", "AsTracking", "TimeProvider",
            "IRunEventNotifier", "RunEvent.Record", "IApplicationMediator", "IGit", "IProcess", "ProcessStart", "IAgent", "IArtifactStore",
            "File.", "Directory.", "Path.Combine", "Path.GetFull", "HttpClient", "Task.Delay", "Include(", "ThenInclude(", "FromSql",
            "ExecuteSql", "Environment.", "Process.", "OrderByDescending", "MaxAsync", "MinAsync", "CountAsync", "LastOrDefault",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_live_status_command_intake_replay_and_every_other_table_are_never_consulted()
    {
        var code = ApplicationCode();

        foreach (var forbidden in new[]
        {
            "GetManualRunAbandonment", "AbandonManualRun", "RunAbandonmentReader.ReadAsync", "RunAbandonmentReader.ReadForProject",
            "RunAbandonmentReader.ReadAll", "RunLifecycleAdmission", "RunIntentRecorder", "ExecutionCapacity", "LocalExecution", "Commands.",
            "CommandHandler", "Latest", "Current", "Attempts", "GitWorkspaces", "GitCheckpoints", "LocalCommitOperations",
            "RepositoryMutationLeases", "VerificationExecutions", "CollaborationMessages", "Artifacts", "CheckpointReviews",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_shared_reason_interpretation_and_coherence_policy_are_reused_and_never_repeated()
    {
        var reader = PolicyFile("RecordedAbandonmentReader.cs");

        Assert.Single(Regex.Matches(reader, @"RunAbandonmentPolicy\.IsCoherent\("));
        Assert.Single(Regex.Matches(reader, @"RunAbandonmentReader\.ReadReason\("));
        foreach (var forbidden in new[] { "IsCanonicalReason", "TryNormalizeReason", "MaximumReasonUtf8Bytes", "AbandonedEvents.Count", "HasNoActiveParticipant ==" })
        {
            Assert.DoesNotContain(forbidden, ApplicationCode(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Only_unreadable_text_and_payload_faults_are_caught_and_no_column_beyond_the_recorded_facts_is_selected()
    {
        var code = ApplicationCode();

        Assert.Single(Regex.Matches(code, @"\bcatch\b"));
        Assert.Equal(
            ["DecoderFallbackException"],
            Regex.Matches(code, @"\bcatch \((\w+)\)").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("ToListAsync", code, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "Objective", "Stage", "AccumulatedAutonomousSeconds", "MaximumReviewCorrectionAttempts", "MaximumAgentAttempts",
            "MaximumAgentInvocationTime", "Requested", "TokenStopThreshold", "TokenWarningThreshold", "NextExecutionNumber", "RootPath",
            "ExecutionNumber",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        // Identifiers, the last advance, the reason and the event facts are bounded raw bytes admitted by the one storage parser; the
        // only typed value a row may hold is the abandonment time read through its own TEXT-only tolerant column mapping.
        var row = PolicyFile("RecordedAbandonmentRow.cs");
        Assert.DoesNotContain("Guid ", row, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(row, "DateTimeOffset"));
        Assert.Contains("DateTimeOffset? AbandonedAtUtc", row, StringComparison.Ordinal);
    }

    [Fact]
    public void One_bounded_statement_proves_the_run_the_project_and_the_exact_run_keyed_abandonment_events()
    {
        var reader = PolicyFile("RecordedAbandonmentReader.cs");

        Assert.Single(Regex.Matches(reader, @"ToArrayAsync\("));
        Assert.Single(Regex.Matches(reader, @"\.Take\(2\)"));
        Assert.Single(Regex.Matches(reader, @"\.DefaultIfEmpty\(\)"));
        Assert.Contains("join project in dbContext.Projects.AsNoTracking() on run.ProjectId equals project.Id", reader, StringComparison.Ordinal);
        Assert.Contains("runEvent.EventType == RunEventType.RunAbandoned", reader, StringComparison.Ordinal);
        Assert.Contains("on run.Id equals abandoned.RunId into abandonedGroup", reader, StringComparison.Ordinal);
    }

    [Fact]
    public void The_endpoint_dispatches_only_through_the_mediator_is_a_bodyless_authorized_get_and_answers_with_an_api_owned_graph()
    {
        var code = ApiCode();
        var endpoint = Assert.Single(Sources(ApiFolders), path => path.EndsWith("Endpoint.cs", StringComparison.Ordinal));
        var source = Code(endpoint);

        Assert.Contains(": RunsBaseEndpoint", source, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"\[HttpGet\("));
        Assert.Contains("abandonment-inspection", source, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"mediator\.SendAsync\("));
        Assert.Contains("[TypeFilter(typeof(RunAbandonmentInspectionGuardFilter))]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromBody]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromQuery]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[AllowAnonymous]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IArtifactStore", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeProvider", code, StringComparison.Ordinal);
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
            BackendRoot(), "DevalCopilot.Api", "Features", "Runs", "GetRunAbandonmentInspection", "RunAbandonmentInspectionGuardFilter.cs"));

        Assert.Contains(": IAsyncResourceFilter", guard, StringComparison.Ordinal);
        Assert.Contains("request.Query.Count > 0", guard, StringComparison.Ordinal);
        Assert.Contains("request.ContentLength is > 0", guard, StringComparison.Ordinal);
        Assert.Contains("HeaderNames.TransferEncoding", guard, StringComparison.Ordinal);
        Assert.Contains("request.Body.ReadAsync(new byte[1]", guard, StringComparison.Ordinal);
        Assert.Contains("RunAbandonmentInspectionErrors.QueryNotAllowed()", guard, StringComparison.Ordinal);
        Assert.Contains("RunAbandonmentInspectionErrors.BodyNotAllowed()", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("mediator", guard, StringComparison.OrdinalIgnoreCase);
    }
}
