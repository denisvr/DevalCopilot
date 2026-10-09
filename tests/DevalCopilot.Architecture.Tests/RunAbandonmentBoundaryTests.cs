using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0031: the explicit abandonment is one Human, metadata-only transition. Only its command handler performs it, it takes the
/// database write lock before any decision and does no external work, its status read never writes, and every one of the eight Agent
/// claim handlers keeps a commit-time lifecycle guard so an abandonment that wins can never be followed by a claim that decided earlier.</summary>
public sealed class RunAbandonmentBoundaryTests
{
    private const string CommandHandler = "DevalCopilot.Application/Features/Runs/Commands/AbandonManualRun/AbandonManualRunCommandHandler.cs";

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

    private static string[] SourcesContaining(string root, string text) => ProductionSources(root)
        .Where(path => Code(path).Contains(text, StringComparison.Ordinal))
        .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string HandlerCode(string name) => Code(Path.Combine(
        BackendRoot(), "DevalCopilot.Application", "Features", "Runs", "Commands", name, $"{name}CommandHandler.cs"));

    [Fact]
    public void Only_the_abandonment_command_handler_performs_the_transition_and_only_the_domain_writes_its_facts()
    {
        var root = BackendRoot();

        Assert.Equal([CommandHandler], SourcesContaining(root, ".Abandon("));
        Assert.Equal(["DevalCopilot.Domain/Features/Runs/Run.cs"], SourcesContaining(root, "AbandonmentReason = "));
        Assert.Equal(["DevalCopilot.Domain/Features/Runs/Run.cs"], SourcesContaining(root, "AbandonedAtUtc = "));
        Assert.Equal(
            [CommandHandler],
            SourcesContaining(root, "RunEventType.RunAbandoned,"));
    }

    [Fact]
    public void The_command_takes_the_write_lock_first_then_decides_then_saves_and_does_no_external_work()
    {
        var code = Code(Path.Combine(BackendRoot(), CommandHandler.Replace('/', Path.DirectorySeparatorChar)));

        var begin = code.IndexOf("BeginTransactionAsync", StringComparison.Ordinal);
        var lockStatement = code.IndexOf("ExecuteUpdateAsync", StringComparison.Ordinal);
        var decision = code.IndexOf("RunAbandonmentAuthority.FindBlockerAsync", StringComparison.Ordinal);
        var save = code.IndexOf("SaveChangesAsync", StringComparison.Ordinal);
        var commit = code.IndexOf("CommitAsync", StringComparison.Ordinal);

        Assert.True(begin >= 0 && begin < lockStatement, "the transaction opens before its locking write");
        Assert.True(lockStatement < decision, "the write lock is taken before the authority is read");
        Assert.True(decision < save && save < commit, "the decision precedes the one save and its commit");
        foreach (var forbidden in new[] { "IArtifactStore", "IGit", "Process", "IAgent", "IAccountUsage", "File.", "Directory." })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_read_only_status_never_writes()
    {
        var root = BackendRoot();
        var query = Code(Path.Combine(
            root, "DevalCopilot.Application", "Features", "Runs", "Queries", "GetManualRunAbandonment", "GetManualRunAbandonmentQueryHandler.cs"));
        var authority = Code(Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Policies", "Abandonment", "RunAbandonmentAuthority.cs"));
        var reader = Code(Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Policies", "Abandonment", "RunAbandonmentReader.cs"));

        foreach (var code in new[] { query, authority, reader })
        {
            foreach (var write in new[] { "SaveChanges", "ExecuteUpdate", "ExecuteDelete", ".Add(", ".Remove(", "BeginTransaction", "IsModified" })
            {
                Assert.DoesNotContain(write, code, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_two_operations_are_single_protected_mvc_endpoints_with_the_body_bound_on_the_write()
    {
        var root = BackendRoot();
        var post = Code(Path.Combine(root, "DevalCopilot.Api", "Features", "Runs", "AbandonManualRun", "AbandonManualRunEndpoint.cs"));
        var get = Code(Path.Combine(root, "DevalCopilot.Api", "Features", "Runs", "GetManualRunAbandonment", "GetManualRunAbandonmentEndpoint.cs"));

        Assert.Contains("[HttpPost(\"{runId:guid}/abandon\")]", post, StringComparison.Ordinal);
        Assert.Contains("[RequestSizeLimit(MaximumRequestBodyBytes)]", post, StringComparison.Ordinal);
        Assert.Contains("MaximumRequestBodyBytes = 8 * 1024", post, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"{runId:guid}/abandonment\")]", get, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnonymous", post + get, StringComparison.Ordinal);
        Assert.Equal(
            ["DevalCopilot.Api/Features/Runs/AbandonManualRun/AbandonManualRunEndpoint.cs"],
            SourcesContaining(root, "[HttpPost(\"{runId:guid}/abandon\")]"));
    }

    [Fact]
    public void The_three_codex_claims_that_can_claim_a_running_run_re_read_the_lifecycle_after_their_guard_writes_and_before_any_insert()
    {
        foreach (var name in new[] { "CreateCodexPlanningAttempt", "CreateChallengeResolutionAttempt", "CreateCodeReviewAttempt" })
        {
            var code = HandlerCode(name);

            var firstGuardWrite = code.IndexOf("CurrentRunExecutionMode.ConfirmAgentAdmittedAsync", StringComparison.Ordinal);
            var lifecycleRead = code.IndexOf("CurrentRunLifecycle.IsStillActiveAsync", StringComparison.Ordinal);
            var refusal = code.IndexOf("CurrentRunLifecycle.Not", StringComparison.Ordinal);
            var insert = code.IndexOf("dbContext.Attempts.Add(attempt)", StringComparison.Ordinal);

            Assert.True(firstGuardWrite >= 0, name);
            Assert.True(firstGuardWrite < lifecycleRead, $"{name} reads the lifecycle only after its write-locking guard");
            Assert.True(lifecycleRead < refusal && refusal < insert, $"{name} refuses before inserting");
            Assert.Equal(lifecycleRead, code.LastIndexOf("CurrentRunLifecycle.IsStillActiveAsync", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Every_other_agent_claim_commits_through_a_lifecycle_token_or_an_in_transaction_lifecycle_read()
    {
        foreach (var name in new[] { "CreateClaudeCriticalReviewAttempt", "CreateImplementationAttempt", "CreateReviewCorrectionAttempt" })
        {
            Assert.Contains("CurrentRunExecutionMode.ReadAndGuardAgentAsync", HandlerCode(name), StringComparison.Ordinal);
        }

        foreach (var name in new[] { "CreateVerificationDiagnosisAttempt", "CreateDiagnosisCorrectionAttempt" })
        {
            Assert.Contains("(RunLifecycle?)candidate.Lifecycle", HandlerCode(name), StringComparison.Ordinal);
        }

        var dispatch = Code(Path.Combine(
            BackendRoot(), "DevalCopilot.Application", "Features", "Runs", "Commands", "MarkAgentAttemptDispatched",
            "MarkAgentAttemptDispatchedCommandHandler.cs"));
        Assert.Contains("run.Lifecycle != RunLifecycle.Running", dispatch, StringComparison.Ordinal);
        Assert.Contains("CurrentRunExecutionMode.ReadAndGuardAgentAsync", dispatch, StringComparison.Ordinal);
    }

    [Fact]
    public void Intake_recognizes_an_abandonment_only_through_the_shared_coherence_reader()
    {
        var root = BackendRoot();

        Assert.Equal(
            [
                "DevalCopilot.Application/Features/Projects/Queries/GetProjectRunSummaries/GetProjectRunSummariesQueryHandler.cs",
                "DevalCopilot.Application/Features/Runs/Commands/AbandonManualRun/AbandonManualRunCommandHandler.cs",
                "DevalCopilot.Application/Features/Runs/Queries/GetManualRunAbandonment/GetManualRunAbandonmentQueryHandler.cs",
                "DevalCopilot.Application/Features/Runs/RunIntentRecorder.cs",
            ],
            SourcesContaining(root, "RunAbandonmentReader."));
        Assert.Equal(
            ["DevalCopilot.Application/Features/Runs/Policies/Abandonment/RunAbandonmentReader.cs"],
            SourcesContaining(root, "RunAbandonmentPolicy.IsCoherent("));
    }
}
