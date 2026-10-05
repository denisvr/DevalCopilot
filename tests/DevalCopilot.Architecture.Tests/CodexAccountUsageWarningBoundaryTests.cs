using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0026: the advisory Codex account-usage warning is read only by its own operations. The one strict-observation consumer
/// beside the stop's gate is the explicit warning query; no claim handler, gate, dispatch guard, supervisor, adapter or invocation path
/// refers to the warning; and the warning code never reuses the stop's gate, facts or decisions or the display-only allowance.</summary>
public sealed class CodexAccountUsageWarningBoundaryTests
{
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

    [Fact]
    public void The_strict_observer_is_consumed_only_by_the_four_claim_handlers_the_stop_gate_the_dispatch_guard_and_the_warning_query()
    {
        var consumers = SourcesContaining(BackendRoot(), "IAccountUsageObserver");

        Assert.Equal(
            [
                "DevalCopilot.Api/HostedServices/CodexAccountUsageDispatchGuard.cs",
                "DevalCopilot.Api/Program.cs",
                "DevalCopilot.Application/Features/Runs/Commands/CreateChallengeResolutionAttempt/CreateChallengeResolutionAttemptCommandHandler.cs",
                "DevalCopilot.Application/Features/Runs/Commands/CreateCodeReviewAttempt/CreateCodeReviewAttemptCommandHandler.cs",
                "DevalCopilot.Application/Features/Runs/Commands/CreateCodexPlanningAttempt/CreateCodexPlanningAttemptCommandHandler.cs",
                "DevalCopilot.Application/Features/Runs/Commands/CreateVerificationDiagnosisAttempt/CreateVerificationDiagnosisAttemptCommandHandler.cs",
                "DevalCopilot.Application/Features/Runs/Policies/CodexAccountUsageStopGate.cs",
                "DevalCopilot.Application/Features/Runs/Ports/IAccountUsageObserver.cs",
                "DevalCopilot.Application/Features/Runs/Queries/GetCodexAccountUsageWarning/GetCodexAccountUsageWarningQueryHandler.cs",
                "DevalCopilot.Infrastructure/Features/EnvironmentReadiness/CodexAccountUsageGuardAdapter.cs",
            ],
            consumers);
    }

    [Fact]
    public void Only_the_warning_operations_the_cockpit_projection_and_the_run_mapping_refer_to_the_warning()
    {
        var referrers = SourcesContaining(BackendRoot(), "AccountUsageWarning");

        Assert.Equal(
            [
                "DevalCopilot.Api/Features/Runs/CodexAccountUsageWarningSettingResponse.cs",
                "DevalCopilot.Api/Features/Runs/GetCodexAccountUsageWarning/CodexAccountUsageWarningWindowResponse.cs",
                "DevalCopilot.Api/Features/Runs/GetCodexAccountUsageWarning/GetCodexAccountUsageWarningEndpoint.cs",
                "DevalCopilot.Api/Features/Runs/GetCodexAccountUsageWarning/GetCodexAccountUsageWarningResponse.cs",
                "DevalCopilot.Api/Features/Runs/GetRunCockpit/GetRunCockpitEndpoint.cs",
                "DevalCopilot.Api/Features/Runs/GetRunCockpit/GetRunCockpitResponse.cs",
                "DevalCopilot.Api/Features/Runs/SetCodexAccountUsageWarning/SetCodexAccountUsageWarningEndpoint.cs",
                "DevalCopilot.Api/Features/Runs/SetCodexAccountUsageWarning/SetCodexAccountUsageWarningRequest.cs",
                "DevalCopilot.Api/Features/Runs/SetCodexAccountUsageWarning/SetCodexAccountUsageWarningRequestConverter.cs",
                "DevalCopilot.Api/Features/Runs/SetCodexAccountUsageWarning/SetCodexAccountUsageWarningResponse.cs",
                "DevalCopilot.Application/Features/Runs/Commands/SetCodexAccountUsageWarning/SetCodexAccountUsageWarningCommand.cs",
                "DevalCopilot.Application/Features/Runs/Commands/SetCodexAccountUsageWarning/SetCodexAccountUsageWarningCommandHandler.cs",
                "DevalCopilot.Application/Features/Runs/Commands/SetCodexAccountUsageWarning/SetCodexAccountUsageWarningCommandResult.cs",
                "DevalCopilot.Application/Features/Runs/Commands/SetCodexAccountUsageWarning/SetCodexAccountUsageWarningCommandValidator.cs",
                "DevalCopilot.Application/Features/Runs/Errors/CodexAccountUsageWarningErrors.cs",
                "DevalCopilot.Application/Features/Runs/Policies/CodexAccountUsageWarningCheckState.cs",
                "DevalCopilot.Application/Features/Runs/Policies/CodexAccountUsageWarningEvaluation.cs",
                "DevalCopilot.Application/Features/Runs/Policies/CodexAccountUsageWarningFact.cs",
                "DevalCopilot.Application/Features/Runs/Policies/CodexAccountUsageWarningPolicy.cs",
                "DevalCopilot.Application/Features/Runs/Policies/CodexAccountUsageWarningReason.cs",
                "DevalCopilot.Application/Features/Runs/Policies/CodexAccountUsageWarningState.cs",
                "DevalCopilot.Application/Features/Runs/Policies/CodexAccountUsageWarningWindow.cs",
                "DevalCopilot.Application/Features/Runs/Policies/CodexAccountUsageWarningWindowKind.cs",
                "DevalCopilot.Application/Features/Runs/Queries/GetCodexAccountUsageWarning/GetCodexAccountUsageWarningQuery.cs",
                "DevalCopilot.Application/Features/Runs/Queries/GetCodexAccountUsageWarning/GetCodexAccountUsageWarningQueryHandler.cs",
                "DevalCopilot.Application/Features/Runs/Queries/GetCodexAccountUsageWarning/GetCodexAccountUsageWarningQueryResult.cs",
                "DevalCopilot.Application/Features/Runs/Queries/GetRunCockpit/GetRunCockpitQueryHandler.cs",
                "DevalCopilot.Application/Features/Runs/Queries/GetRunCockpit/GetRunCockpitQueryResult.cs",
                "DevalCopilot.Domain/Features/Runs/CodexAccountUsageWarning.cs",
                "DevalCopilot.Domain/Features/Runs/CodexAccountUsageWarningReading.cs",
                "DevalCopilot.Domain/Features/Runs/Run.cs",
                "DevalCopilot.Domain/Features/Runs/RunEventType.cs",
                "DevalCopilot.Infrastructure/Configurations/Runs/RunConfiguration.cs",
            ],
            referrers);
    }

    [Fact]
    public void The_stored_warning_is_read_only_by_the_run_its_mapping_the_set_command_and_the_check_query()
    {
        var readers = SourcesContaining(BackendRoot(), "CodexAccountUsageWarningStorageProperty");

        Assert.Equal(
            [
                "DevalCopilot.Application/Features/Runs/Commands/SetCodexAccountUsageWarning/SetCodexAccountUsageWarningCommandHandler.cs",
                "DevalCopilot.Application/Features/Runs/Queries/GetCodexAccountUsageWarning/GetCodexAccountUsageWarningQueryHandler.cs",
                "DevalCopilot.Domain/Features/Runs/Run.cs",
                "DevalCopilot.Infrastructure/Configurations/Runs/RunConfiguration.cs",
            ],
            readers);
    }

    [Fact]
    public void No_claim_handler_gate_guard_supervisor_adapter_or_invocation_path_refers_to_the_warning()
    {
        var root = BackendRoot();
        var guarded = new[]
        {
            Path.Combine(root, "DevalCopilot.Api", "HostedServices"),
            Path.Combine(root, "DevalCopilot.Infrastructure", "Features"),
            Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Commands", "CreateCodexPlanningAttempt"),
            Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Commands", "CreateChallengeResolutionAttempt"),
            Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Commands", "CreateCodeReviewAttempt"),
            Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Commands", "CreateVerificationDiagnosisAttempt"),
            Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Commands", "CreateImplementationAttempt"),
            Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Commands", "CreateClaudeCriticalReviewAttempt"),
            Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Commands", "CreateReviewCorrectionAttempt"),
            Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Commands", "MarkAgentAttemptDispatched"),
            Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Commands", "RecordCodexAccountUsageStop"),
            Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Queries", "GetCodexAccountUsageStopPlan"),
        };

        foreach (var directory in guarded)
        {
            Assert.True(Directory.Exists(directory), directory);
            Assert.Empty(SourcesContaining(directory, "AccountUsageWarning"));
        }

        var policies = Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Policies");
        foreach (var stopFile in new[]
                 {
                     "CodexAccountUsageStopGate.cs", "CodexAccountUsageStopPolicy.cs", "CodexAccountUsageStopFact.cs",
                     "CodexAccountUsageGuardFacts.cs", "CodexAccountUsageClaimGuard.cs", "CodexAccountUsageDecisionFact.cs",
                 })
        {
            Assert.DoesNotContain("AccountUsageWarning", Code(Path.Combine(policies, stopFile)), StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            SourcesContaining(Path.Combine(root, "DevalCopilot.Domain", "Features", "Runs"), "CodexAccountUsageWarning"),
            path => path.EndsWith("Attempt.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void The_warning_never_reuses_the_stop_gate_facts_decisions_or_the_display_only_allowance()
    {
        var root = BackendRoot();
        var warningSources = ProductionSources(root)
            .Where(path => Path.GetFileName(path).Contains("AccountUsageWarning", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}Api{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.EndsWith("RunEventType.cs", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(warningSources);

        foreach (var forbidden in new[]
                 {
                     "CodexAccountUsageStop", "CodexAccountUsageGuardFacts", "CodexAccountUsageLaunchTuple", "AgentCodexAccountUsageDecision",
                     "CodexAccountUsageDecision", "ICodexAccountAllowanceAdapter", "CodexAccountAllowanceObservation",
                     "GetCodexAccountAllowanceQuery", "ExpectedAccountUsageGuard", "CodexAccountUsageDispatchGuard",
                 })
        {
            foreach (var path in warningSources)
            {
                Assert.DoesNotContain(forbidden, Code(path), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_advisory_column_is_never_declared_a_concurrency_token()
    {
        var configuration = Code(Path.Combine(
            BackendRoot(), "DevalCopilot.Infrastructure", "Configurations", "Runs", "RunConfiguration.cs"));
        var start = configuration.IndexOf("Run.CodexAccountUsageWarningStorageProperty", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var statement = configuration[start..configuration.IndexOf(';', start)];

        Assert.DoesNotContain("IsConcurrencyToken", statement, StringComparison.Ordinal);
    }
}
