using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0024: new Agent delivery of tracked change evidence comes only from attested snapshots. The raw working-path patch
/// (<c>CompleteDiff</c>) belongs only to the ordinary capture behind the checkpoint fingerprint (ADR-0027: no query or endpoint reads it); no claim handler, builder or Runs policy may read it, the
/// attestation is derived only at the claim handlers, and supervisors, endpoints and adapters never touch the attestation types, so a
/// sealed manifest can never be rebuilt from current files at dispatch.</summary>
public sealed class TrackedAttestationBoundaryTests
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

    /// <summary>The source with comment lines removed, so documentation may name a member without being a reader of it.</summary>
    private static string Code(string path) => string.Join(
        '\n', File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Fact]
    public void No_runs_feature_source_reads_the_raw_working_path_patch()
    {
        var root = BackendRoot();
        var offenders = ProductionSources(Path.Combine(root, "DevalCopilot.Application", "Features", "Runs"))
            .Where(path => Code(path).Contains("CompleteDiff", StringComparison.Ordinal))
            .Select(path => Relative(root, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_production_source_reads_the_raw_working_path_patch_of_a_capture_ADR_0027()
    {
        var root = BackendRoot();
        var readers = ProductionSources(root)
            .Where(path => Code(path).Contains(".CompleteDiff", StringComparison.Ordinal))
            .Select(path => Relative(root, path))
            .Order()
            .ToArray();

        // The ordinary capture still produces the patch for the checkpoint fingerprint, but no endpoint, query, policy or builder
        // reads it any more: checkpoint inspection is attested like new Agent delivery.
        Assert.Empty(readers);
    }

    [Fact]
    public void Checkpoint_inspection_uses_only_the_explicit_inspection_capture_and_never_the_ordinary_one()
    {
        var root = BackendRoot();
        var handler = Code(Path.Combine(
            root, "DevalCopilot.Application", "Features", "Projects", "Queries", "GetGitCheckpointDiff", "GetGitCheckpointDiffQueryHandler.cs"));

        Assert.Contains("CaptureForCheckpointInspectionAsync(", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureAsync(", handler.Replace("CaptureForCheckpointInspectionAsync(", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureWithUntrackedPreviewsAsync(", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureForAgentContextAsync(", handler, StringComparison.Ordinal);
        Assert.Contains("TrackedSourcePurpose.HumanInspection", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_tracked_source_purpose_is_stated_by_exactly_its_own_consumer_and_Agent_delivery_never_names_the_human_one()
    {
        var root = BackendRoot();
        var derivers = ProductionSources(root)
            .Where(path => Code(path).Contains("AttestedTrackedComparison.Derive(", StringComparison.Ordinal))
            .Select(path => Relative(root, path))
            .Order()
            .ToArray();
        var humanPurpose = ProductionSources(root)
            .Where(path => Code(path).Contains("TrackedSourcePurpose.HumanInspection", StringComparison.Ordinal))
            .Select(path => Relative(root, path))
            .ToArray();
        var agentPurpose = ProductionSources(root)
            .Where(path => Code(path).Contains("TrackedSourcePurpose.AgentDelivery", StringComparison.Ordinal))
            .Select(path => Relative(root, path))
            .Order()
            .ToArray();

        Assert.Equal(
            [
                Path.Combine("DevalCopilot.Application", "Features", "Projects", "Queries", "GetGitCheckpointDiff", "GetGitCheckpointDiffQueryHandler.cs"),
                Path.Combine("DevalCopilot.Application", "Features", "Runs", "Policies", "TrackedChangeEvidence.cs"),
            ],
            derivers);
        Assert.Equal(
            [Path.Combine("DevalCopilot.Application", "Features", "Projects", "Queries", "GetGitCheckpointDiff", "GetGitCheckpointDiffQueryHandler.cs")],
            humanPurpose);
        // The shared policy names the Agent purpose only to apply its reservation; the one Agent consumer states it.
        Assert.Equal(
            [
                Path.Combine("DevalCopilot.Application", "Features", "Projects", "Policies", "AttestedTrackedComparison.cs"),
                Path.Combine("DevalCopilot.Application", "Features", "Runs", "Policies", "TrackedChangeEvidence.cs"),
            ],
            agentPurpose);
    }

    [Fact]
    public void The_shared_comparison_policy_lives_with_Projects_and_Runs_owns_no_second_comparison_algorithm()
    {
        var root = BackendRoot();
        var projects = Path.Combine(root, "DevalCopilot.Application", "Features", "Projects", "Policies");
        var runs = Path.Combine(root, "DevalCopilot.Application", "Features", "Runs");

        Assert.True(File.Exists(Path.Combine(projects, "TrackedComparison.cs")));
        Assert.True(File.Exists(Path.Combine(projects, "AttestedTrackedComparison.cs")));
        var runsSources = ProductionSources(runs).ToArray();
        Assert.DoesNotContain(runsSources, path => Path.GetFileName(path) == "TrackedComparison.cs");
        Assert.DoesNotContain(runsSources, path => Code(path).Contains("class TrackedComparison", StringComparison.Ordinal));
        Assert.DoesNotContain(runsSources, path => Code(path).Contains("static string? Compose(", StringComparison.Ordinal));
    }

    [Fact]
    public void The_attestation_is_derived_only_by_the_claim_handlers()
    {
        var root = BackendRoot();
        var callers = ProductionSources(root)
            .Where(path => File.ReadAllText(path).Contains("TrackedChangeEvidence.From(", StringComparison.Ordinal))
            .Select(path => Relative(root, path))
            .Order()
            .ToArray();

        var expected = ClaimHandlers
            .Where(handler => !handler.Contains("CreateCodexPlanningAttempt", StringComparison.Ordinal))
            .Select(handler => Path.Combine("DevalCopilot.Application", "Features", handler))
            .Order()
            .ToArray();
        Assert.Equal(expected, callers);
    }

    [Fact]
    public void Composed_tracked_evidence_is_constructed_only_by_its_own_derivation_so_no_caller_can_hand_text_to_a_builder()
    {
        var root = BackendRoot();
        var constructors = ProductionSources(root)
            .Where(path => Code(path).Contains("new TrackedChangeEvidence(", StringComparison.Ordinal))
            .Select(path => Relative(root, path))
            .ToArray();

        Assert.Equal(
            [Path.Combine("DevalCopilot.Application", "Features", "Runs", "Policies", "TrackedChangeEvidence.cs")],
            constructors);
    }

    [Fact]
    public void No_supervisor_endpoint_or_adapter_names_the_attestation_types_so_a_sealed_manifest_cannot_be_rebuilt_at_dispatch()
    {
        var root = BackendRoot();
        string[] forbidden = ["TrackedChangeEvidence", "GitWorkspaceTrackedFile", "GitWorkspaceTrackedOmission", "TrackedComparison"];
        var offenders = ProductionSources(root)
            .Where(path => Relative(root, path).StartsWith("DevalCopilot.Api", StringComparison.Ordinal))
            .Where(path => forbidden.Any(name => File.ReadAllText(path).Contains(name, StringComparison.Ordinal)))
            .Select(path => Relative(root, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Only_the_capture_reader_constructs_attested_facts_in_infrastructure_and_nothing_else_does()
    {
        var root = BackendRoot();
        var constructors = ProductionSources(root)
            .Where(path => !Relative(root, path).StartsWith("DevalCopilot.Application", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("GitWorkspaceTrackedFile", StringComparison.Ordinal))
            .Select(path => Relative(root, path))
            .Order()
            .ToArray();

        Assert.Equal(
            [
                Path.Combine("DevalCopilot.Infrastructure", "Features", "Projects", "GitWorkspaceEvidenceReader.TrackedFiles.cs"),
                Path.Combine("DevalCopilot.Infrastructure", "Features", "Projects", "TrackedFileSourceReader.cs"),
            ],
            constructors);
    }

    [Fact]
    public void The_manifest_reduction_ladder_has_no_raw_diff_input_and_no_instruction_name_special_case_left_over()
    {
        var root = BackendRoot();
        var manifest = File.ReadAllText(Path.Combine(
            root, "DevalCopilot.Application", "Features", "Runs", "Policies", "ChangeEvidenceManifest.cs"));

        Assert.DoesNotContain("string? completeDiff", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("WithholdReservedDiff", manifest, StringComparison.Ordinal);
        Assert.Contains("TrackedChangeEvidence tracked", manifest, StringComparison.Ordinal);
    }
}
