using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0021: project instruction context is captured and assembled only when a new Agent claim seals its manifest.
/// A supervisor, dispatch, restart reconciliation, adapter, endpoint, or query must never rebuild a sealed manifest from
/// current files, so the capture and the section assembly may be referenced only by the eight claim handlers (and the
/// Projects capability that implements the capture).</summary>
public sealed class InstructionContextCaptureBoundaryTests
{
    private static readonly string[] ClaimHandlers =
    [
        @"Runs\Commands\CreateCodexPlanningAttempt\CreateCodexPlanningAttemptCommandHandler.cs",
        @"Runs\Commands\CreateClaudeCriticalReviewAttempt\CreateClaudeCriticalReviewAttemptCommandHandler.cs",
        @"Runs\Commands\CreateChallengeResolutionAttempt\CreateChallengeResolutionAttemptCommandHandler.cs",
        @"Runs\Commands\CreateImplementationAttempt\CreateImplementationAttemptCommandHandler.cs",
        @"Runs\Commands\CreateCodeReviewAttempt\CreateCodeReviewAttemptCommandHandler.cs",
        @"Runs\Commands\CreateReviewCorrectionAttempt\CreateReviewCorrectionAttemptCommandHandler.cs",
        @"Runs\Commands\CreateDiagnosisCorrectionAttempt\CreateDiagnosisCorrectionAttemptCommandHandler.cs",
        @"Runs\Commands\CreateVerificationDiagnosisAttempt\CreateVerificationDiagnosisAttemptCommandHandler.cs",
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

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path);

    [Fact]
    public void The_agent_context_capture_is_requested_only_by_the_eight_claim_handlers()
    {
        var root = BackendRoot();
        var callers = ProductionSources(root)
            .Where(path => File.ReadAllText(path).Contains("CaptureForAgentContextAsync(", StringComparison.Ordinal))
            .Select(path => Relative(root, path))
            .Where(relative => !relative.EndsWith("IGitWorkspaceEvidenceReader.cs", StringComparison.Ordinal)
                && !relative.Contains("GitWorkspaceEvidenceReader", StringComparison.Ordinal))
            .Order()
            .ToArray();

        var expected = ClaimHandlers.Select(handler => Path.Combine("DevalCopilot.Application", "Features", handler)).Order().ToArray();
        Assert.Equal(expected, callers);
    }

    [Fact]
    public void The_instruction_section_is_prepared_only_by_the_claim_handlers_and_never_by_dispatch_or_reads()
    {
        var root = BackendRoot();
        var callers = ProductionSources(root)
            .Where(path => File.ReadAllText(path).Contains("ProjectInstructionContextManifest.Prepare(", StringComparison.Ordinal))
            .Select(path => Relative(root, path))
            .Order()
            .ToArray();

        var expected = ClaimHandlers.Select(handler => Path.Combine("DevalCopilot.Application", "Features", handler)).Order().ToArray();
        Assert.Equal(expected, callers);
        Assert.DoesNotContain(callers, caller => caller.StartsWith("DevalCopilot.Api", StringComparison.Ordinal));
        Assert.DoesNotContain(callers, caller => caller.StartsWith("DevalCopilot.Infrastructure", StringComparison.Ordinal));
    }

    [Fact]
    public void No_supervisor_or_adapter_names_the_instruction_types_so_a_sealed_manifest_cannot_be_rebuilt_at_dispatch()
    {
        var root = BackendRoot();
        string[] forbidden = ["ProjectInstructionContextManifest", "GitWorkspaceInstructionContext", "projectInstructionContext"];
        var offenders = ProductionSources(root)
            .Where(path => Relative(root, path).StartsWith("DevalCopilot.Api", StringComparison.Ordinal)
                || Relative(root, path).StartsWith(Path.Combine("DevalCopilot.Infrastructure", "Features", "Runs"), StringComparison.Ordinal)
                || Relative(root, path).StartsWith(Path.Combine("DevalCopilot.Infrastructure", "Features", "Processes"), StringComparison.Ordinal))
            .Where(path => forbidden.Any(word => File.ReadAllText(path).Contains(word, StringComparison.Ordinal)))
            .Select(path => Relative(root, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_new_manifest_names_the_fixed_documentation_references_any_more()
    {
        var root = BackendRoot();
        var offenders = ProductionSources(root)
            .Where(path => File.ReadAllText(path).Contains("instructionReferences", StringComparison.OrdinalIgnoreCase))
            .Select(path => Relative(root, path))
            .ToArray();

        Assert.Empty(offenders);
    }
}
