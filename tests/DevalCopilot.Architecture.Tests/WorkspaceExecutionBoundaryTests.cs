using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0057: workspace execution exclusion is one shared Application policy over existing rows. Every Agent claim handler and
/// the verification claim read it before and again at their write-locked commit seam, both single-use dispatch gates and the eligibility
/// feeds read it, the policy itself only reads existing durable rows (no Git, provider, process, observation or write), and it adds no
/// persistence of its own.</summary>
public sealed class WorkspaceExecutionBoundaryTests
{
    private const string PolicyFolder = "DevalCopilot.Application/Features/Projects/Policies/WorkspaceExecutions/";

    private static readonly string[] AgentClaimHandlers =
    [
        "DevalCopilot.Application/Features/Runs/Commands/CreateChallengeResolutionAttempt/CreateChallengeResolutionAttemptCommandHandler.cs",
        "DevalCopilot.Application/Features/Runs/Commands/CreateClaudeCriticalReviewAttempt/CreateClaudeCriticalReviewAttemptCommandHandler.cs",
        "DevalCopilot.Application/Features/Runs/Commands/CreateCodeReviewAttempt/CreateCodeReviewAttemptCommandHandler.cs",
        "DevalCopilot.Application/Features/Runs/Commands/CreateCodexPlanningAttempt/CreateCodexPlanningAttemptCommandHandler.cs",
        "DevalCopilot.Application/Features/Runs/Commands/CreateDiagnosisCorrectionAttempt/CreateDiagnosisCorrectionAttemptCommandHandler.cs",
        "DevalCopilot.Application/Features/Runs/Commands/CreateImplementationAttempt/CreateImplementationAttemptCommandHandler.cs",
        "DevalCopilot.Application/Features/Runs/Commands/CreateReviewCorrectionAttempt/CreateReviewCorrectionAttemptCommandHandler.cs",
        "DevalCopilot.Application/Features/Runs/Commands/CreateVerificationDiagnosisAttempt/CreateVerificationDiagnosisAttemptCommandHandler.cs",
    ];

    private static readonly string[] AgentFeeds =
    [
        "DevalCopilot.Application/Features/Runs/Queries/GetEligibleAgentAttempts/GetEligibleAgentAttemptsQueryHandler.cs",
        "DevalCopilot.Application/Features/Runs/Queries/GetEligibleChallengeResolutionAttempts/GetEligibleChallengeResolutionAttemptsQueryHandler.cs",
        "DevalCopilot.Application/Features/Runs/Queries/GetEligibleClaudeCriticalReviewAttempts/GetEligibleClaudeCriticalReviewAttemptsQueryHandler.cs",
        "DevalCopilot.Application/Features/Runs/Queries/GetEligibleCodeReviewAttempts/GetEligibleCodeReviewAttemptsQueryHandler.cs",
        "DevalCopilot.Application/Features/Runs/Queries/GetEligibleImplementationAttempts/GetEligibleImplementationAttemptsQueryHandler.cs",
        "DevalCopilot.Application/Features/Runs/Queries/GetEligibleReviewCorrectionAttempts/GetEligibleReviewCorrectionAttemptsQueryHandler.cs",
        "DevalCopilot.Application/Features/Runs/Queries/GetEligibleVerificationDiagnosisAttempts/GetEligibleVerificationDiagnosisAttemptsQueryHandler.cs",
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
            && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Code(string path) => string.Join(
        '\n', File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
            && !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

    private static string CodeOf(string root, string source) => Code(Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar)));

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static int Count(string text, string token)
    {
        var count = 0;
        for (var index = text.IndexOf(token, StringComparison.Ordinal); index >= 0; index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void Exactly_the_eight_Agent_claim_handlers_create_Agent_attempts_and_each_reads_the_policy_early_and_at_its_commit_seam()
    {
        var root = BackendRoot();

        var claimers = ProductionSources(root)
            .Select(path => Relative(root, path))
            .Where(path => path.StartsWith("DevalCopilot.Application/Features/Runs/Commands/", StringComparison.Ordinal)
                && CodeOf(root, path).Contains("Attempt.ClaimAgent", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AgentClaimHandlers.Order(StringComparer.Ordinal), claimers);
        // At least one early read and one read at each durable commit seam: the critical-review handler has its ordinary, repair and
        // coordinated-step seams, so it reads the policy more often than the others.
        foreach (var handler in AgentClaimHandlers)
        {
            var code = CodeOf(root, handler);
            Assert.True(Count(code, "WorkspaceExecutionFence.CheckClaimAsync") >= 2, handler);
        }

        Assert.True(Count(CodeOf(root, AgentClaimHandlers[1]), "WorkspaceExecutionFence.CheckClaimAsync") >= 4);
    }

    [Fact]
    public void The_verification_claim_the_dispatch_authority_and_the_Agent_dispatch_gate_read_the_policy()
    {
        var root = BackendRoot();

        Assert.Contains(
            "WorkspaceExecutionFence.CheckClaimAsync",
            CodeOf(root, "DevalCopilot.Application/Features/Projects/Commands/ClaimVerificationExecution/ClaimVerificationExecutionCommandHandler.cs"));
        var authority = CodeOf(root, "DevalCopilot.Application/Features/Projects/Commands/MarkVerificationExecutionDispatched/VerificationDispatchAuthority.cs");
        Assert.Contains("WorkspaceExecutionFence.CheckVerificationDispatchAsync", authority);
        Assert.Contains(
            "WorkspaceExecutionFence.CheckAgentDispatchAsync",
            CodeOf(root, "DevalCopilot.Application/Features/Runs/Commands/MarkAgentAttemptDispatched/MarkAgentAttemptDispatchedCommandHandler.cs"));
        Assert.Contains("WorkspaceExecutionPolicy.AssessAsync", CodeOf(root, "DevalCopilot.Application/Features/Runs/Policies/LocalCommit/LocalCommitAuthorityReader.cs"));

        // Both pre-dispatch decisions share the one verification authority, so they can never disagree about the owner.
        foreach (var decision in new[]
        {
            "DevalCopilot.Application/Features/Projects/Commands/MarkVerificationExecutionDispatched/MarkVerificationExecutionDispatchedCommandHandler.cs",
            "DevalCopilot.Application/Features/Projects/Commands/RecordVerificationExecutionSourceChanged/RecordVerificationExecutionSourceChangedCommandHandler.cs",
        })
        {
            Assert.Contains("VerificationDispatchAuthority.ConfirmAsync", CodeOf(root, decision));
        }
    }

    [Fact]
    public void The_eligibility_feeds_only_filter_known_conflicts_and_never_decide_them()
    {
        var root = BackendRoot();

        foreach (var feed in AgentFeeds)
        {
            Assert.Contains("WorkspaceExecutionFeed.WithoutKnownWorkspaceConflictsAsync", CodeOf(root, feed));
        }

        Assert.Contains(
            "WorkspaceExecutionFence.CheckVerificationDispatchAsync",
            CodeOf(root, "DevalCopilot.Application/Features/Projects/Queries/GetEligibleVerificationExecutions/GetEligibleVerificationExecutionsQueryHandler.cs"));
    }

    [Fact]
    public void The_policy_only_reads_existing_rows_and_touches_no_git_provider_process_observation_or_write()
    {
        var root = BackendRoot();
        var sources = ProductionSources(root).Select(path => Relative(root, path))
            .Where(path => path.StartsWith(PolicyFolder, StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(sources);
        string[] forbidden =
        [
            "AgentSupervisions", "IGitWorkspaceEvidenceReader", "IProcessExecutionAdapter", "ICodexPlanningAdapter", "IClaude",
            "IApplicationMediator", "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "ExecuteSql",
            "File.", "Directory.", "Process.", "BeginTransaction", "TimeProvider", "DateTimeOffset.UtcNow", ".Complete(", ".Interrupt(",
            ".Fail(", "MarkAgentDispatched", "Release(",
        ];

        foreach (var source in sources)
        {
            var code = CodeOf(root, source);
            foreach (var token in forbidden)
            {
                Assert.False(code.Contains(token, StringComparison.Ordinal), $"{source} contains {token}");
            }
        }
    }

    [Fact]
    public void The_exclusion_adds_no_persistence_of_its_own()
    {
        var root = BackendRoot();

        var persistence = ProductionSources(root)
            .Select(path => Relative(root, path))
            .Where(path => path.StartsWith("DevalCopilot.Infrastructure/", StringComparison.Ordinal)
                && File.ReadAllText(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)))
                    .Contains("WorkspaceExecution", StringComparison.Ordinal))
            .ToArray();
        var domain = ProductionSources(root)
            .Select(path => Relative(root, path))
            .Where(path => path.StartsWith("DevalCopilot.Domain/", StringComparison.Ordinal)
                && path.Contains("WorkspaceExecution", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(persistence);
        Assert.Empty(domain);
    }

    [Fact]
    public void The_status_operation_is_one_protected_bodyless_GET_that_dispatches_only_the_query()
    {
        var root = BackendRoot();
        var endpoint = CodeOf(
            root, "DevalCopilot.Api/Features/Projects/GetWorkspaceExecutionStatus/GetWorkspaceExecutionStatusEndpoint.cs");

        Assert.Contains(": ProjectsBaseEndpoint", endpoint);
        Assert.Contains("[HttpGet(\"{projectId:guid}/workspace-execution-status\")]", endpoint);
        Assert.Contains("WorkspaceExecutionStatusBodylessGuardFilter", endpoint);
        Assert.Contains("new GetWorkspaceExecutionStatusQuery(projectId)", endpoint);
        Assert.DoesNotContain("[FromBody]", endpoint);
        Assert.DoesNotContain("[FromQuery]", endpoint);
        Assert.DoesNotContain("[AllowAnonymous]", endpoint);
        Assert.DoesNotContain("DbContext", endpoint);
        Assert.DoesNotContain("WorkspaceExecutionPolicy", endpoint);
    }
}
