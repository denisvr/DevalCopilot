using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The bounded, versioned manifest a diagnosis seals (ADR-0018): fixed host text before the untrusted-evidence boundary,
/// verification metadata without executable path, argument, storage path or hash, verified failure excerpts with exact
/// labels, the output schema, and a whole-document 32 KiB ceiling reached by stepping the excerpt budget down.
/// </summary>
public sealed class VerificationDiagnosisContextManifestBuilderTests : IAsyncLifetime
{
    private static readonly Guid PlanId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ReportId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static Spec Failed(string stdout, string stderr) => new(Kind.Failed, Stdout: Bytes(stdout), Stderr: Bytes(stderr));

    private static async Task<string> BuildAsync(DiagnosisTestScene scene, string planContent = "{\"scope\":\"Ledger\"}")
    {
        await using var db = scene.Fixture.CreateContext();
        var read = await VerificationDiagnosisEvidence.ReadAsync(
            db, scene.Run.ProjectId, scene.Scene.Workspace.Id, scene.Implementation.ReviewCheckpoint, asNoTracking: true, CancellationToken.None);
        var prefixes = await VerificationFailureExcerpts.ReadVerifiedPrefixesAsync(scene.Store, read.Value!, CancellationToken.None);
        var evidence = UntrackedManifestTestSupport.Evidence(scene.Implementation.ReviewFingerprint);
        return VerificationDiagnosisContextManifestBuilder.Build(
            scene.Run.ProjectId, scene.Scene.Workspace.Id, scene.Implementation.ReviewCheckpoint.Id, scene.Implementation.ReviewFingerprint,
            "Fix the ledger query.", PlanId, "Plan summary.", planContent,
            ReportId, "Report summary.", "{\"completedWork\":\"Done.\",\"verification\":\"dotnet test\"}",
            read.Value!, prefixes.Streams!, evidence.ChangedPaths, TrackedChangeEvidence.From(evidence),
            ProjectInstructionContextManifest.Prepare(
                scene.Scene.Workspace.Id, scene.Implementation.ReviewCheckpoint.Id, scene.Implementation.ReviewFingerprint, evidence.InstructionContext),
            evidence.UntrackedFiles);
    }

    [Fact]
    public async Task The_document_has_the_exact_member_order_with_host_text_before_the_untrusted_boundary()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);

        using var manifest = JsonDocument.Parse(await BuildAsync(scene));

        var names = manifest.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(
            [
                "protocolVersion", "expectedResponseContract", "objective", "projectId", "gitWorkspaceId", "resultGitCheckpointId",
                "resultCheckpointFingerprintSha256", "instruction", "failureOutputNotice", "expectedOutputSchema",
                "untrustedEvidenceBoundary", "projectInstructionContextBoundary", "projectInstructionContext", "implementedPlan", "executionReport", "verificationEvidence", "changeEvidence",
            ],
            names);
        var boundary = Array.IndexOf(names, "untrustedEvidenceBoundary");
        Assert.True(Array.IndexOf(names, "instruction") < boundary);
        Assert.True(Array.IndexOf(names, "failureOutputNotice") < boundary);
        Assert.True(Array.IndexOf(names, "expectedOutputSchema") < boundary);
        foreach (var untrusted in new[] { "implementedPlan", "executionReport", "verificationEvidence", "changeEvidence" })
        {
            Assert.True(Array.IndexOf(names, untrusted) > boundary, untrusted);
            Assert.Contains(untrusted, manifest.RootElement.GetProperty("untrustedEvidenceBoundary").GetString(), StringComparison.Ordinal);
        }

        Assert.Equal("VerificationDiagnosis", manifest.RootElement.GetProperty("expectedResponseContract").GetString());
        Assert.Equal(VerificationDiagnosisContextManifestBuilder.Instruction, manifest.RootElement.GetProperty("instruction").GetString());
        Assert.Equal(VerificationDiagnosisContextManifestBuilder.ExcerptNotice, manifest.RootElement.GetProperty("failureOutputNotice").GetString());
    }

    [Fact]
    public async Task The_manifest_embeds_the_diagnosis_output_schema_that_has_no_approval_shape()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed()]);

        using var manifest = JsonDocument.Parse(await BuildAsync(scene));

        var schema = manifest.RootElement.GetProperty("expectedOutputSchema");
        Assert.Equal(
            JsonSerializer.Serialize(VerificationDiagnosisOutputSchema.BuildSchemaDocument()),
            JsonSerializer.Serialize(schema));
        Assert.Equal(
            ["findings", "escalation"],
            schema.GetProperty("properties").GetProperty("outcome").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("approv", schema.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_executable_path_argument_storage_path_or_hash_reaches_the_manifest()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed(), Spec.Failed()]);
        var manifest = await BuildAsync(scene);

        await using var db = _fixture.CreateContext();
        var rows = await db.VerificationOutputArtifacts.AsNoTracking().ToListAsync();
        Assert.NotEmpty(rows);

        Assert.DoesNotContain(DiagnosisTestScene.ExecutableSentinel, manifest, StringComparison.Ordinal);
        Assert.DoesNotContain(DiagnosisTestScene.ArgumentSentinel, manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("runner.exe", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("sha256:", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("output.sealed", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("verifications/", manifest, StringComparison.Ordinal);
        foreach (var row in rows)
        {
            Assert.DoesNotContain(row.RelativeStoragePath, manifest, StringComparison.Ordinal);
            Assert.DoesNotContain(row.ContentHash, manifest, StringComparison.Ordinal);
            Assert.DoesNotContain(row.ContentHash["sha256:".Length..], manifest, StringComparison.Ordinal);
            Assert.DoesNotContain(row.Id.ToString(), manifest, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var command in scene.Commands)
        {
            Assert.DoesNotContain(command.ExecutablePath, manifest, StringComparison.Ordinal);
            Assert.DoesNotContain(command.Id.ToString(), manifest, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Verification_metadata_is_name_number_status_outcome_and_exit_code_with_excerpts_only_for_failures()
    {
        var scene = await CreateAsync(
            _fixture, [new Spec(Kind.Passed, Name: "Backend tests"), new Spec(Kind.Failed, ExitCode: 3, Name: "Frontend tests",
                Stdout: Bytes("stdout text"), Stderr: Bytes("stderr text"))]);

        using var manifest = JsonDocument.Parse(await BuildAsync(scene));

        var evidence = manifest.RootElement.GetProperty("verificationEvidence").EnumerateArray().ToArray();
        Assert.Equal(2, evidence.Length);
        Assert.Equal(
            ["commandName", "commandNumber", "executionNumber", "status", "outcome", "exitCode", "failureOutput"],
            evidence[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal("Backend tests", evidence[0].GetProperty("commandName").GetString());
        Assert.Equal(1, evidence[0].GetProperty("commandNumber").GetInt32());
        Assert.Equal("Passed", evidence[0].GetProperty("status").GetString());
        Assert.Equal("Exited", evidence[0].GetProperty("outcome").GetString());
        Assert.Equal(0, evidence[0].GetProperty("exitCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, evidence[0].GetProperty("failureOutput").ValueKind);

        Assert.Equal("Failed", evidence[1].GetProperty("status").GetString());
        Assert.Equal(3, evidence[1].GetProperty("exitCode").GetInt32());
        var stdout = evidence[1].GetProperty("failureOutput").GetProperty("standardOutput");
        Assert.Equal(
            ["stream", "capturedBytes", "captureTruncation", "excerptState", "excerptBytes", "text"],
            stdout.EnumerateObject().Select(p => p.Name));
        Assert.Equal("standardOutput", stdout.GetProperty("stream").GetString());
        Assert.Equal("stdout text", stdout.GetProperty("text").GetString());
        Assert.Equal("complete", stdout.GetProperty("excerptState").GetString());
        Assert.Equal("notTruncated", stdout.GetProperty("captureTruncation").GetString());
        Assert.Equal(11, stdout.GetProperty("capturedBytes").GetInt64());
        Assert.Equal("stderr text", evidence[1].GetProperty("failureOutput").GetProperty("standardError").GetProperty("text").GetString());
    }

    [Fact]
    public async Task The_plan_and_report_are_carried_as_untrusted_structured_evidence_and_the_checkpoint_identity_is_pinned()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed()]);

        using var manifest = JsonDocument.Parse(await BuildAsync(scene, "{\"scope\":\"Revised ledger\"}"));

        var plan = manifest.RootElement.GetProperty("implementedPlan");
        Assert.Equal(PlanId, plan.GetProperty("messageId").GetGuid());
        Assert.Equal("Revised ledger", plan.GetProperty("structuredContent").GetProperty("scope").GetString());
        Assert.Equal(ReportId, manifest.RootElement.GetProperty("executionReport").GetProperty("messageId").GetGuid());
        Assert.Equal(scene.Implementation.ReviewCheckpoint.Id, manifest.RootElement.GetProperty("resultGitCheckpointId").GetGuid());
        Assert.Equal(scene.Implementation.ReviewFingerprint, manifest.RootElement.GetProperty("resultCheckpointFingerprintSha256").GetString());
    }

    [Fact]
    public async Task A_typical_manifest_stays_within_the_ceiling_with_the_full_excerpt_budget()
    {
        var scene = await CreateAsync(_fixture, [Failed(new string('a', 3000), new string('b', 3000)), Spec.Passed()]);

        var manifest = await BuildAsync(scene);

        Assert.True(Encoding.UTF8.GetByteCount(manifest) <= 32768);
        using var document = JsonDocument.Parse(manifest);
        var failure = document.RootElement.GetProperty("verificationEvidence")[0].GetProperty("failureOutput");
        Assert.Equal(2048, failure.GetProperty("standardOutput").GetProperty("excerptBytes").GetInt32());
        Assert.Equal(2048, failure.GetProperty("standardError").GetProperty("excerptBytes").GetInt32());
    }

    [Fact]
    public async Task A_manifest_whose_escaped_excerpts_exceed_the_ceiling_steps_the_excerpt_budget_down_and_labels_the_omissions()
    {
        // '<' is escaped to six bytes by the JSON writer: twelve 2 KiB streams of it cannot fit 32 KiB at a 12 KiB budget.
        var escaped = new string('<', 2048);
        var scene = await CreateAsync(
            _fixture, [Failed(escaped, escaped), Failed(escaped, escaped), Failed(escaped, escaped), Failed(escaped, escaped)]);

        var manifest = await BuildAsync(scene);

        Assert.True(Encoding.UTF8.GetByteCount(manifest) <= 32768, $"{Encoding.UTF8.GetByteCount(manifest)} bytes");
        using var document = JsonDocument.Parse(manifest);
        var streams = document.RootElement.GetProperty("verificationEvidence").EnumerateArray()
            .SelectMany(entry => new[]
            {
                entry.GetProperty("failureOutput").GetProperty("standardOutput"),
                entry.GetProperty("failureOutput").GetProperty("standardError"),
            })
            .ToArray();
        var omitted = streams.Where(s => s.GetProperty("excerptState").GetString() == "omittedByBudget").ToArray();
        Assert.NotEmpty(omitted);
        Assert.All(omitted, s =>
        {
            Assert.Equal(string.Empty, s.GetProperty("text").GetString());
            Assert.Equal(0, s.GetProperty("excerptBytes").GetInt32());
            Assert.Equal(2048, s.GetProperty("capturedBytes").GetInt64());
        });
        // The omission is the tail: allocation order is preserved after stepping down.
        var states = streams.Select(s => s.GetProperty("excerptState").GetString()).ToArray();
        var firstOmitted = Array.IndexOf(states, "omittedByBudget");
        Assert.All(states.Skip(firstOmitted), state => Assert.Equal("omittedByBudget", state));
    }

    [Fact]
    public async Task Non_ascii_excerpt_text_round_trips_through_the_manifest_unchanged_when_it_fits()
    {
        var scene = await CreateAsync(_fixture, [Failed("fail: ünïcode — 😀", "err")]);

        using var document = JsonDocument.Parse(await BuildAsync(scene));

        Assert.Equal(
            "fail: ünïcode — 😀",
            document.RootElement.GetProperty("verificationEvidence")[0].GetProperty("failureOutput").GetProperty("standardOutput").GetProperty("text").GetString());
    }

    [Fact]
    public async Task The_untracked_previews_sit_under_change_evidence_after_the_boundary()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed()]);

        var manifest = await BuildAsync(scene);

        UntrackedManifestTestSupport.AssertManifestCarriesPreviews(manifest);
    }
}
