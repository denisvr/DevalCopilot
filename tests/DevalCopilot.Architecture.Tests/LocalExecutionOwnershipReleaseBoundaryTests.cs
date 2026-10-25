using System.Reflection;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>
/// ADR-0065: the three append-only facts have exactly one writer each, the opaque dispatch ticket can only be minted where the marker
/// commits, the release reading is one read-only statement with no external port, the release command performs no I/O under its write
/// lock, and no host service or API type can fabricate provenance.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseBoundaryTests
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

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static IReadOnlyList<string> FilesContaining(string token)
    {
        var root = BackendRoot();
        return ProductionSources(root)
            .Where(path => Code(path).Contains(token, StringComparison.Ordinal))
            .Select(path => Relative(root, path))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void Each_append_only_fact_has_exactly_the_one_writer_ADR_0065_names()
    {
        Assert.Equal(
            ["DevalCopilot.Application/Features/Runs/Policies/LocalExecutionSequences/LocalExecutionDispatchWitnessing.cs"],
            FilesContaining("LocalExecutionDispatchWitnesses.Add("));
        Assert.Equal(
            [
                "DevalCopilot.Application/Features/Projects/Commands/RecordVerificationExecutionResult/RecordVerificationExecutionResultCommandHandler.cs",
                "DevalCopilot.Application/Features/Runs/Commands/RecordImplementationResult/RecordImplementationResultCommandHandler.cs",
                "DevalCopilot.Application/Features/Runs/Commands/RecordImplementationReviewResult/RecordImplementationReviewResultCommandHandler.cs",
                "DevalCopilot.Application/Features/Runs/Commands/RecordReviewCorrectionResult/RecordReviewCorrectionResultCommandHandler.cs",
                "DevalCopilot.Application/Features/Runs/Commands/RecordVerificationDiagnosisResult/RecordVerificationDiagnosisResultCommandHandler.cs",
            ],
            FilesContaining("LocalExecutionTerminationObservations.Add("));
        Assert.Equal(
            ["DevalCopilot.Application/Features/Runs/Commands/ReleaseLocalExecutionOwnership/ReleaseLocalExecutionOwnershipCommandHandler.cs"],
            FilesContaining("LocalExecutionOwnershipReleases.Add("));
    }

    [Fact]
    public void No_production_code_updates_or_deletes_any_of_the_three_facts()
    {
        foreach (var set in new[] { "LocalExecutionDispatchWitnesses", "LocalExecutionTerminationObservations", "LocalExecutionOwnershipReleases" })
        {
            Assert.Empty(FilesContaining($"{set}.Remove("));
            Assert.Empty(FilesContaining($"{set}.RemoveRange("));
            Assert.Empty(FilesContaining($"{set}.Update("));
            Assert.Empty(FilesContaining($"{set}.ExecuteUpdate"));
            Assert.Empty(FilesContaining($"{set}.ExecuteDelete"));
        }
    }

    [Fact]
    public void The_opaque_dispatch_ticket_cannot_be_constructed_outside_the_marker_witnessing_policy()
    {
        var constructors = typeof(LocalExecutionDispatchTicket).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(constructors);
        Assert.Equal(
            ["DevalCopilot.Application/Features/Runs/Policies/LocalExecutionSequences/LocalExecutionDispatchWitnessing.cs"],
            FilesContaining("new LocalExecutionDispatchTicket("));
        // No member exposes the private provenance publicly: the type has no public readable state at all.
        Assert.Empty(typeof(LocalExecutionDispatchTicket).GetProperties(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(typeof(LocalExecutionHostLaunch).GetProperties(BindingFlags.Public | BindingFlags.Instance));
        Assert.DoesNotContain("Nonce", typeof(LocalExecutionDispatchTicket).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_host_supervisors_and_the_result_commands_carry_a_ticket_and_no_api_contract_names_one()
    {
        var root = BackendRoot();
        var carriers = FilesContaining("LocalExecutionDispatchTicket");
        Assert.NotEmpty(carriers);
        var outsideApplication = carriers.Where(path => !path.StartsWith("DevalCopilot.Application/", StringComparison.Ordinal)).ToList();
        // Only the host's supervisors, which hold the ticket in memory from the dispatch marker to the result recorder, may reference it.
        Assert.All(
            outsideApplication,
            path => Assert.StartsWith("DevalCopilot.Api/HostedServices/", path, StringComparison.Ordinal));
        foreach (var folder in new[] { Path.Combine(root, "DevalCopilot.Api", "Features"), Path.Combine(root, "DevalCopilot.Infrastructure") })
        {
            foreach (var path in ProductionSources(folder))
            {
                Assert.DoesNotContain("DispatchTicket", Code(path), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_release_reading_and_command_use_no_external_port_and_the_reading_writes_nothing()
    {
        var root = BackendRoot();
        var reading = ProductionSources(Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Policies", "LocalExecutionOwnershipReleases"))
            .Concat(ProductionSources(Path.Combine(root, "DevalCopilot.Application", "Features", "Runs", "Queries", "GetLocalExecutionOwnershipRelease")))
            .ToList();
        Assert.NotEmpty(reading);
        foreach (var path in reading)
        {
            var code = Code(path);
            Assert.DoesNotContain("SaveChanges", code, StringComparison.Ordinal);
            Assert.DoesNotContain("BeginTransaction", code, StringComparison.Ordinal);
            Assert.DoesNotContain("ExecuteUpdate", code, StringComparison.Ordinal);
            Assert.DoesNotContain(".Add(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("TrackAll", code, StringComparison.Ordinal);
            AssertNoExternalPort(code, path);
        }

        var command = Code(Path.Combine(
            root, "DevalCopilot.Application", "Features", "Runs", "Commands", "ReleaseLocalExecutionOwnership",
            "ReleaseLocalExecutionOwnershipCommandHandler.cs"));
        AssertNoExternalPort(command, "ReleaseLocalExecutionOwnershipCommandHandler.cs");
        Assert.DoesNotContain("Task.Run", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", command, StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_endpoints_dispatch_through_the_application_mediator_only()
    {
        var root = BackendRoot();
        foreach (var folder in new[] { "GetLocalExecutionOwnershipRelease", "ReleaseLocalExecutionOwnership" })
        {
            foreach (var path in ProductionSources(Path.Combine(root, "DevalCopilot.Api", "Features", "Runs", folder)))
            {
                var code = Code(path);
                Assert.DoesNotContain("DbContext", code, StringComparison.Ordinal);
                Assert.DoesNotContain("IProcessExecutionAdapter", code, StringComparison.Ordinal);
                Assert.DoesNotContain("Infrastructure", code, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_live_fence_and_every_shared_ownership_consumer_reach_the_release_proof_only_through_the_one_ownership_definition()
    {
        // The chain predicate is defined in one place and consumed only by the ownership definition and the live status reading.
        Assert.Equal(
            [
                "DevalCopilot.Application/Features/Runs/Policies/LocalExecutionSequences/LocalExecutionOwnership.cs",
                "DevalCopilot.Application/Features/Runs/Queries/GetLocalExecutionSequence/GetLocalExecutionSequenceQueryHandler.cs",
            ],
            FilesContaining("LocalExecutionReleaseChain.").Where(path => !path.EndsWith("LocalExecutionReleaseChain.cs", StringComparison.Ordinal)).ToList());
    }

    private static void AssertNoExternalPort(string code, string path)
    {
        foreach (var port in new[]
                 {
                     "IGitWorkspaceEvidenceReader", "IProcessExecutionAdapter", "IArtifactStore", "IClaudeImplementationAdapter",
                     "ICodexImplementationReviewAdapter", "IGitHubOriginObserver", "System.Diagnostics.Process",
                 })
        {
            Assert.False(code.Contains(port, StringComparison.Ordinal), $"{path} references the external port {port}.");
        }
    }
}
