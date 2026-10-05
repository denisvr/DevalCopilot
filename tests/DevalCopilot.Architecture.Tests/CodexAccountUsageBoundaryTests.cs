using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0025: the run-scoped Codex account-usage stop is enforced only where it is selected. Four Codex claim handlers check it
/// at claim, four Codex supervisors guard it through one shared helper before dispatch, one dedicated command records the terminal
/// outcome, and the strict observation never reuses the display-only allowance projection. No HTTP contract accepts the internal guard
/// facts, and no other source can snapshot or record a decision.</summary>
public sealed class CodexAccountUsageBoundaryTests
{
    private static readonly string[] CodexClaimHandlers =
    [
        Path.Combine("Runs", "Commands", "CreateCodexPlanningAttempt", "CreateCodexPlanningAttemptCommandHandler.cs"),
        Path.Combine("Runs", "Commands", "CreateChallengeResolutionAttempt", "CreateChallengeResolutionAttemptCommandHandler.cs"),
        Path.Combine("Runs", "Commands", "CreateCodeReviewAttempt", "CreateCodeReviewAttemptCommandHandler.cs"),
        Path.Combine("Runs", "Commands", "CreateVerificationDiagnosisAttempt", "CreateVerificationDiagnosisAttemptCommandHandler.cs"),
    ];

    private static readonly string[] CodexSupervisors =
    [
        "AgentAttemptSupervisor.cs",
        "ChallengeResolutionSupervisor.cs",
        "ImplementationReviewSupervisor.cs",
        "VerificationDiagnosisSupervisor.cs",
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

    private static string[] SourcesContaining(string root, string text) => ProductionSources(root)
        .Where(path => Code(path).Contains(text, StringComparison.Ordinal))
        .Select(path => Path.GetRelativePath(root, path))
        .Order()
        .ToArray();

    [Fact]
    public void Only_the_four_codex_claim_handlers_call_the_claim_gate()
    {
        var root = BackendRoot();
        var application = Path.Combine(root, "DevalCopilot.Application", "Features");

        foreach (var member in new[] { "CodexAccountUsageStopGate.CheckClaimAsync", "CodexAccountUsageStopGate.ConfirmInTransactionAsync", "CodexAccountUsageStopGate.Snapshot" })
        {
            var callers = SourcesContaining(application, member);

            Assert.Equal(CodexClaimHandlers.Select(path => path).Order(StringComparer.Ordinal).ToArray(), callers.Order(StringComparer.Ordinal).ToArray());
        }
    }

    [Fact]
    public void Only_the_four_codex_supervisors_and_the_host_composition_use_the_shared_dispatch_guard()
    {
        var root = BackendRoot();
        var users = SourcesContaining(Path.Combine(root, "DevalCopilot.Api"), "CodexAccountUsageDispatchGuard")
            .Select(path => Path.GetFileName(path))
            .Order()
            .ToArray();

        Assert.Equal(
            CodexSupervisors.Concat(new[] { "CodexAccountUsageDispatchGuard.cs", "Program.cs" }).Order(StringComparer.Ordinal).ToArray(),
            users.Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("ClaudeCriticalReviewSupervisor.cs", users);
        Assert.DoesNotContain("ImplementationSupervisor.cs", users);
        Assert.DoesNotContain("ReviewCorrectionSupervisor.cs", users);
    }

    [Fact]
    public void The_dispatch_gate_receives_guard_facts_only_from_the_codex_supervisors()
    {
        var root = BackendRoot();
        var users = SourcesContaining(root, "ExpectedAccountUsageGuard").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            CodexSupervisors.Concat(new[] { "MarkAgentAttemptDispatchedCommand.cs", "MarkAgentAttemptDispatchedCommandHandler.cs" }).Order(StringComparer.Ordinal).ToArray(),
            users);
    }

    [Fact]
    public void No_http_contract_accepts_or_returns_the_internal_guard_facts_or_the_terminal_recording_command()
    {
        var root = BackendRoot();
        var features = Path.Combine(root, "DevalCopilot.Api", "Features");

        foreach (var internalName in new[]
                 {
                     "CodexAccountUsageGuardFacts", "RecordCodexAccountUsageStopCommand", "ExpectedAccountUsageGuard",
                     "AccountUsageObservation", "IAccountUsageObserver", "CodexAccountUsageStopPolicy",
                 })
        {
            Assert.Empty(SourcesContaining(features, internalName));
        }
    }

    [Fact]
    public void The_terminal_stop_transition_and_the_threshold_snapshot_have_exactly_one_production_caller_each()
    {
        var root = BackendRoot();

        Assert.Equal(
            [Path.Combine("DevalCopilot.Application", "Features", "Runs", "Commands", "RecordCodexAccountUsageStop", "RecordCodexAccountUsageStopCommandHandler.cs")],
            SourcesContaining(root, ".CompleteAgentAccountUsageStop("));
        Assert.Equal(
            [Path.Combine("DevalCopilot.Application", "Features", "Runs", "Policies", "CodexAccountUsageStopGate.cs")],
            SourcesContaining(root, ".SnapshotCodexAccountUsageStop("));
    }

    [Fact]
    public void The_strict_observation_never_uses_the_display_only_allowance_projection()
    {
        var root = BackendRoot();
        var runsAndGuard = new[]
        {
            Path.Combine(root, "DevalCopilot.Application", "Features", "Runs"),
            Path.Combine(root, "DevalCopilot.Api", "HostedServices"),
        };

        foreach (var directory in runsAndGuard)
        {
            Assert.Empty(SourcesContaining(directory, "ICodexAccountAllowanceAdapter"));
            Assert.Empty(SourcesContaining(directory, "CodexAccountAllowanceObservation"));
            Assert.Empty(SourcesContaining(directory, "GetCodexAccountAllowanceQuery"));
        }

        var adapter = Code(Path.Combine(root, "DevalCopilot.Infrastructure", "Features", "EnvironmentReadiness", "CodexAccountUsageGuardAdapter.cs"));
        Assert.DoesNotContain("CodexAccountAllowance", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void The_strict_adapter_is_the_only_implementation_of_the_port_and_lives_in_infrastructure()
    {
        var root = BackendRoot();

        Assert.Equal(
            [Path.Combine("DevalCopilot.Infrastructure", "Features", "EnvironmentReadiness", "CodexAccountUsageGuardAdapter.cs")],
            SourcesContaining(root, ": IAccountUsageObserver"));
    }
}
