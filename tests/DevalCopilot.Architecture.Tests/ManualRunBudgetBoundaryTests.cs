using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0028: the run-wide Agent claim and reserved-time ceilings are chosen once, at manual run creation, and are read
/// from the persisted Run by all eight Agent claim handlers. Nothing else assigns, changes or substitutes them.</summary>
public sealed class ManualRunBudgetBoundaryTests
{
    private static readonly string[] ClaimHandlers =
    [
        "CreateChallengeResolutionAttempt",
        "CreateClaudeCriticalReviewAttempt",
        "CreateCodeReviewAttempt",
        "CreateCodexPlanningAttempt",
        "CreateDiagnosisCorrectionAttempt",
        "CreateImplementationAttempt",
        "CreateReviewCorrectionAttempt",
        "CreateVerificationDiagnosisAttempt",
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
        .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
        .Order(StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void All_eight_claim_handlers_compare_against_the_persisted_run_ceilings_and_never_a_constant()
    {
        var root = BackendRoot();
        foreach (var name in ClaimHandlers)
        {
            var path = Path.Combine(
                root, "DevalCopilot.Application", "Features", "Runs", "Commands", name, $"{name}CommandHandler.cs");
            var code = Code(path);

            Assert.Contains("run.MaximumAgentAttempts", code, StringComparison.Ordinal);
            Assert.Contains("run.MaximumAgentInvocationTime", code, StringComparison.Ordinal);
            Assert.DoesNotContain("DefaultMaximumAgent", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Only_the_creation_operations_and_the_domain_name_the_default_ceilings()
    {
        var referrers = SourcesContaining(BackendRoot(), "DefaultMaximumAgent");

        Assert.Equal(
            [
                "DevalCopilot.Application/Features/Runs/Commands/CreateManualRun/CreateManualRunCommandHandler.cs",
                "DevalCopilot.Application/Features/Runs/Commands/StartSimulatedRun/StartSimulatedRunCommandHandler.cs",
                "DevalCopilot.Domain/Features/Runs/Run.cs",
            ],
            referrers);
    }

    [Fact]
    public void Only_the_shared_intent_recorder_creates_a_classified_run_and_nothing_assigns_a_ceiling_afterwards()
    {
        var root = BackendRoot();

        Assert.Equal(
            [
                "DevalCopilot.Application/Features/Runs/RunIntentRecorder.cs",
                "DevalCopilot.Domain/Features/Runs/Run.cs",
            ],
            SourcesContaining(root, "RecordClassifiedIntent("));
        Assert.Equal(
            ["DevalCopilot.Domain/Features/Runs/Run.cs"],
            SourcesContaining(root, "MaximumAgentAttempts = maximumAgentAttempts"));
        Assert.Equal(
            ["DevalCopilot.Domain/Features/Runs/Run.cs"],
            SourcesContaining(root, "MaximumAgentInvocationTime = maximumAgentInvocationTime"));
    }

    [Fact]
    public void Only_the_manual_creation_operation_accepts_the_budget_choices()
    {
        Assert.Equal(
            [
                "DevalCopilot.Api/Features/Runs/CreateManualRun/CreateManualRunEndpoint.cs",
                "DevalCopilot.Api/Features/Runs/CreateManualRun/CreateManualRunRequest.cs",
                "DevalCopilot.Application/Features/Runs/Commands/CreateManualRun/CreateManualRunCommand.cs",
                "DevalCopilot.Application/Features/Runs/Commands/CreateManualRun/CreateManualRunCommandHandler.cs",
                "DevalCopilot.Application/Features/Runs/Commands/CreateManualRun/CreateManualRunCommandValidator.cs",
                "DevalCopilot.Application/Features/Runs/Commands/CreateManualRun/ManualRunBudgetRange.cs",
            ],
            SourcesContaining(BackendRoot(), "MaximumAgentInvocationMinutes"));
    }
}
