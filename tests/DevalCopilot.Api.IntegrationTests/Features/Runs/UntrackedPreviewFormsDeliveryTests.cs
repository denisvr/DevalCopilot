using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.RealAdapterInstructionProbe;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Physically proven untracked-file previews (ADR-0022) at the narrowest shared boundaries: the real Git capture over a real
/// worktree whose untracked paths are real hard links to an outside file, EVERY manifest form that carries generic untracked
/// previews, the real artifact store, and the REAL provider adapter of that form's role (both providers) over a recording
/// process double. For each form the outside sentinel is absent from the entire returned evidence, the entire sealed manifest
/// and the adapter's actual standard input, while an unrelated safe sibling is delivered and the unsafe paths stay accounted for
/// with the fixed <c>containment_unproven</c> omission. Planning requests no previews and is covered with the claims.
/// </summary>
public sealed class UntrackedPreviewFormsDeliveryTests : IDisposable
{
    private static readonly Guid Id = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly UntrackedPreviewScene _scene = UntrackedPreviewScene.Create();
    private readonly FilesystemArtifactStore _store;
    private readonly string _launch;
    private readonly GitWorkspaceEvidenceReader _reader = new(new ChildProcessExecutionAdapter());

    public UntrackedPreviewFormsDeliveryTests()
    {
        _store = new FilesystemArtifactStore(Path.Combine(_scene.Root, "artifacts"));
        _launch = Path.Combine(_scene.Root, "provider.exe");
        File.WriteAllText(_launch, string.Empty);
    }

    public void Dispose() => _scene.Dispose();

    private static readonly ImplementationContextManifestBuilder.VerificationCommandReference[] Commands = [new("Backend tests", true)];

    private static readonly CodeReviewContextManifestBuilder.VerificationEvidence[] Verification =
        [new("Backend tests", 1, "Passed", "Exited", 0)];

    private static readonly CodeReviewContextManifestBuilder.CorrectionEvidence Correction = new(
        Id, "previous", "{\"completedWork\":\"before\"}",
        [new CodeReviewContextManifestBuilder.CorrectionFinding(Id, "finding", "{\"severity\":\"high\"}")],
        [new CodeReviewContextManifestBuilder.CorrectionRevisionResponse(Id, Id, "response", "{\"decision\":\"fixed\"}")]);

    private const string DirectGuidance = "Prefer the smaller change.";

    private static readonly PlanningImplementationAuthorizationFact Authorization = new(Id, Id, Id, Id, "Because the plan is sound.");

    /// <summary>Every builder entry point that accepts generic untracked previews, with its format-repair flavor where one exists,
    /// and the provider contract (hence the real adapter) that its manifest is sealed for.</summary>
    internal static readonly (string Form, AgentResponseContract Contract, AgentProvider Provider)[] Forms =
    [
        ("critical-review", AgentResponseContract.CriticalReview, AgentProvider.ClaudeCode),
        ("critical-review-repair", AgentResponseContract.CriticalReview, AgentProvider.ClaudeCode),
        ("resolution", AgentResponseContract.ChallengeResolution, AgentProvider.Codex),
        ("resolution-repair", AgentResponseContract.ChallengeResolution, AgentProvider.Codex),
        ("implementation-accepted", AgentResponseContract.ImplementationReport, AgentProvider.ClaudeCode),
        ("implementation-accepted-guided", AgentResponseContract.ImplementationReport, AgentProvider.ClaudeCode),
        ("implementation-revised", AgentResponseContract.ImplementationReport, AgentProvider.ClaudeCode),
        ("implementation-revised-reviewed", AgentResponseContract.ImplementationReport, AgentProvider.ClaudeCode),
        ("implementation-authorized", AgentResponseContract.ImplementationReport, AgentProvider.ClaudeCode),
        ("implementation-authorized-guided", AgentResponseContract.ImplementationReport, AgentProvider.ClaudeCode),
        ("code-review", AgentResponseContract.ImplementationReview, AgentProvider.Codex),
        ("code-review-repair", AgentResponseContract.ImplementationReview, AgentProvider.Codex),
        ("code-review-correction", AgentResponseContract.ImplementationReview, AgentProvider.Codex),
        ("code-review-correction-repair", AgentResponseContract.ImplementationReview, AgentProvider.Codex),
        ("review-correction", AgentResponseContract.ReviewCorrection, AgentProvider.ClaudeCode),
        ("review-correction-human-guidance", AgentResponseContract.ReviewCorrection, AgentProvider.ClaudeCode),
        ("review-correction-direct-guidance", AgentResponseContract.ReviewCorrection, AgentProvider.ClaudeCode),
        ("diagnosis-correction-guided", AgentResponseContract.ReviewCorrection, AgentProvider.ClaudeCode),
        ("verification-diagnosis", AgentResponseContract.VerificationDiagnosis, AgentProvider.Codex),
    ];

    public static TheoryData<string> FormNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var (form, _, _) in Forms)
            {
                data.Add(form);
            }

            return data;
        }
    }

    private static string Build(string form, GitWorkspaceEvidenceResult result)
    {
        var paths = result.ChangedPaths;
        var diff = result.CompleteDiff;
        var fingerprint = result.FingerprintSha256!;
        var untracked = result.UntrackedFiles;
        var instructions = ProjectInstructionContextManifest.Prepare(Id, Id, fingerprint, result.InstructionContext);
        const string plan = "{\"scope\":\"Ledger\"}";
        var decisions = new[] { new ImplementationContextManifestBuilder.DecisionEvidence(Id, "Decision.", "{\"decision\":\"accept\"}") };
        var authorization = new ImplementationContextManifestBuilder.HumanAuthorizationEvidence(Id, Id, Id, Authorization.Rationale);
        var acceptance = new ImplementationContextManifestBuilder.AcceptanceEvidence("Accepted.", "{\"rationale\":\"Sound.\"}");
        var finding = new ReviewCorrectionContextManifestBuilder.Finding(Id, "Finding.", "{\"severity\":\"high\"}");
        var challenge = new[] { new ChallengeResolutionContextManifestBuilder.ChallengeEvidence(Id, "c", "{}") };

        return form switch
        {
            "critical-review" => ClaudeCriticalReviewContextManifestBuilder.Build(
                Id, Id, Id, fingerprint, "objective", Id, "summary", plan, paths, diff, instructions, untrackedFiles: untracked),
            "critical-review-repair" => ClaudeCriticalReviewContextManifestBuilder.Build(
                Id, Id, Id, fingerprint, "objective", Id, "summary", plan, paths, diff, instructions, untrackedFiles: untracked,
                formatRepair: true),
            "resolution" => ChallengeResolutionContextManifestBuilder.Build(
                Id, Id, Id, fingerprint, "objective", Id, "summary", plan, challenge, paths, diff, instructions, untrackedFiles: untracked),
            "resolution-repair" => ChallengeResolutionContextManifestBuilder.Build(
                Id, Id, Id, fingerprint, "objective", Id, "summary", plan, challenge, paths, diff, instructions, untrackedFiles: untracked,
                formatRepair: true),
            "implementation-accepted" => ImplementationContextManifestBuilder.BuildForAcceptedOriginalProposal(
                Id, Id, Id, fingerprint, "objective", Id, "summary", plan, acceptance, paths, diff, Commands, instructions, untracked),
            "implementation-accepted-guided" => ImplementationContextManifestBuilder.BuildForAcceptedOriginalProposal(
                Id, Id, Id, fingerprint, "objective", Id, "summary", plan, acceptance, paths, diff, Commands, instructions, untracked,
                directHumanGuidance: DirectGuidance),
            "implementation-revised" => ImplementationContextManifestBuilder.BuildForResolvedRevisedProposal(
                Id, Id, Id, fingerprint, "objective", Id, "summary", plan, decisions, paths, diff, Commands, instructions,
                untrackedFiles: untracked),
            "implementation-revised-reviewed" => ImplementationContextManifestBuilder.BuildForResolvedRevisedProposal(
                Id, Id, Id, fingerprint, "objective", Id, "summary", plan, decisions, paths, diff, Commands, instructions, acceptance,
                untracked),
            "implementation-authorized" => ImplementationContextManifestBuilder.BuildForHumanAuthorizedEscalatedProposal(
                Id, Id, Id, fingerprint, "objective", Id, "summary", plan, decisions, authorization, paths, diff, Commands, instructions,
                untracked),
            "implementation-authorized-guided" => ImplementationContextManifestBuilder.BuildForHumanAuthorizedEscalatedProposal(
                Id, Id, Id, fingerprint, "objective", Id, "summary", plan, decisions, authorization, paths, diff, Commands, instructions,
                untracked, directHumanGuidance: DirectGuidance),
            "code-review" => CodeReviewContextManifestBuilder.Build(
                Id, Id, Id, fingerprint, "objective", Id, "plan", "{}", Id, "report", "{}", Verification, paths, diff, instructions, untracked),
            "code-review-repair" => CodeReviewContextManifestBuilder.Build(
                Id, Id, Id, fingerprint, "objective", Id, "plan", "{}", Id, "report", "{}", Verification, paths, diff, instructions, untracked,
                formatRepair: true),
            "code-review-correction" => CodeReviewContextManifestBuilder.BuildForCorrection(
                Id, Id, Id, fingerprint, "objective", Id, "plan", "{}", Id, "report", "{}", Verification, paths, diff, Correction,
                instructions, untracked),
            "code-review-correction-repair" => CodeReviewContextManifestBuilder.BuildForCorrection(
                Id, Id, Id, fingerprint, "objective", Id, "plan", "{}", Id, "report", "{}", Verification, paths, diff, Correction,
                instructions, untracked, formatRepair: true),
            "review-correction" => ReviewCorrectionContextManifestBuilder.Build(
                Id, Id, Id, Id, fingerprint, "objective", Id, "report", "{}", [finding], paths, diff, instructions, untrackedFiles: untracked),
            "review-correction-human-guidance" => ReviewCorrectionContextManifestBuilder.Build(
                Id, Id, Id, Id, fingerprint, "objective", Id, "report", "{}", [finding], paths, diff, instructions,
                new ReviewCorrectionContextManifestBuilder.Guidance(Id, "Authorized advice."), untracked),
            "review-correction-direct-guidance" => ReviewCorrectionContextManifestBuilder.Build(
                Id, Id, Id, Id, fingerprint, "objective", Id, "report", "{}", [finding], paths, diff, instructions,
                untrackedFiles: untracked, directHumanGuidance: DirectGuidance),
            "diagnosis-correction-guided" => ReviewCorrectionContextManifestBuilder.Build(
                Id, Id, Id, Id, fingerprint, "objective", Id, "report", "{}", [finding], paths, diff, instructions,
                untrackedFiles: untracked, directHumanGuidance: DirectGuidance,
                sourceNotice: ReviewCorrectionContextManifestBuilder.VerificationDiagnosisSourceNotice),
            "verification-diagnosis" => VerificationDiagnosisContextManifestBuilder.Build(
                Id, Id, Id, fingerprint, "objective", Id, "plan", plan, Id, "report", "{}",
                new VerificationDiagnosisEvidence.Selection([]), [], paths, diff, instructions, untracked),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };
    }

    [Fact]
    public void The_closed_list_covers_every_form_that_carries_previews_and_both_providers()
    {
        Assert.Equal(Forms.Length, Forms.Select(entry => entry.Form).Distinct().Count());
        Assert.Contains(Forms, entry => entry.Provider == AgentProvider.ClaudeCode);
        Assert.Contains(Forms, entry => entry.Provider == AgentProvider.Codex);
        // Every contract that is sealed with change evidence has at least one form here; planning requests no previews.
        Assert.Equal(
            [
                AgentResponseContract.ChallengeResolution, AgentResponseContract.CriticalReview, AgentResponseContract.ImplementationReport,
                AgentResponseContract.ImplementationReview, AgentResponseContract.ReviewCorrection, AgentResponseContract.VerificationDiagnosis,
            ],
            Forms.Select(entry => entry.Contract).Distinct().OrderBy(contract => contract.ToString(), StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(FormNames))]
    public async Task The_outside_sentinel_reaches_no_surface_of_any_form_while_the_safe_sibling_does(string form)
    {
        _scene.AddOutsideHardLinks();
        _scene.AddSafeSiblings();
        var (_, contract, provider) = Forms.Single(entry => entry.Form == form);

        var result = await _reader.CaptureForAgentContextAsync(_scene.Repository, includeUntrackedPreviews: true, CancellationToken.None);
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        var manifest = Build(form, result);
        var delivery = await SealAndDeliverAsync(
            manifest, contract, provider, form.StartsWith("implementation-authorized", StringComparison.Ordinal) ? Authorization : null,
            AdapterVersion(form, contract), IsDirectlyGuided(form) ? DirectGuidance : null);

        // The unsafe files really are the outside file under a second name: the parent reader would have returned its bytes.
        Assert.Equal(UntrackedPreviewScene.Sentinel, File.ReadAllText(Path.Combine(_scene.Repository, "linked.txt")));
        Assert.Equal(delivery.SealedManifest, delivery.Stdin);
        Assert.Equal(manifest, delivery.Stdin);
        Assert.DoesNotContain(UntrackedPreviewScene.Sentinel, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain(UntrackedPreviewScene.Sentinel, delivery.SealedManifest, StringComparison.Ordinal);
        Assert.DoesNotContain(UntrackedPreviewScene.Sentinel, delivery.Stdin, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(delivery.Stdin, UntrackedPreviewScene.SafeText));
        Assert.Equal(1, Occurrences(delivery.Stdin, UntrackedPreviewScene.ControlText));
        AssertUntrackedAccounting(delivery.Stdin);
    }

    [Fact]
    public async Task A_manifest_sealed_from_a_capture_without_the_hard_links_is_unchanged_in_shape_and_carries_every_preview()
    {
        _scene.AddSafeSiblings();
        _scene.Write("plain.txt", "PLAIN-UNTRACKED-3a8c an ordinary untracked file");

        var result = await _reader.CaptureForAgentContextAsync(_scene.Repository, includeUntrackedPreviews: true, CancellationToken.None);
        var delivery = await SealAndDeliverAsync(Build("critical-review", result), AgentResponseContract.CriticalReview, AgentProvider.ClaudeCode);

        using var document = JsonDocument.Parse(delivery.Stdin);
        var files = document.RootElement.GetProperty("changeEvidence").GetProperty("untrackedFiles").GetProperty("files").EnumerateArray().ToArray();
        Assert.All(files, file =>
        {
            Assert.Equal("included", file.GetProperty("preview").GetString());
            Assert.Equal(JsonValueKind.Null, file.GetProperty("omissionReason").ValueKind);
            Assert.True(file.GetProperty("contentComplete").GetBoolean());
        });
        Assert.Equal(3, files.Length);
    }

    /// <summary>The linked paths are named with the fixed reason and neither text nor size; the siblings are included whole.</summary>
    private static void AssertUntrackedAccounting(string manifestText)
    {
        using var document = JsonDocument.Parse(manifestText);
        var section = document.RootElement.GetProperty("changeEvidence").GetProperty("untrackedFiles");
        var files = section.GetProperty("files").EnumerateArray().ToDictionary(file => file.GetProperty("path").GetString()!);
        Assert.Equal(
            ["deep/control.txt", "deep/nested/linked.txt", "linked.txt", "safe-sibling.txt"],
            files.Keys.OrderBy(path => path, StringComparer.Ordinal));
        foreach (var linked in UntrackedPreviewScene.LinkedPaths)
        {
            var entry = files[linked];
            Assert.Equal("omitted", entry.GetProperty("preview").GetString());
            Assert.Equal("containment_unproven", entry.GetProperty("omissionReason").GetString());
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("sizeBytes").ValueKind);
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("text").ValueKind);
            Assert.False(entry.GetProperty("contentComplete").GetBoolean());
        }

        Assert.Equal(UntrackedPreviewScene.SafeText, files["safe-sibling.txt"].GetProperty("text").GetString());
        Assert.Equal(UntrackedPreviewScene.ControlText, files["deep/control.txt"].GetProperty("text").GetString());
        Assert.True(files["safe-sibling.txt"].GetProperty("contentComplete").GetBoolean());
        Assert.False(section.GetProperty("allFilesComplete").GetBoolean());
    }

    private async Task<Delivery> SealAndDeliverAsync(
        string manifest, AgentResponseContract contract, AgentProvider provider, PlanningImplementationAuthorizationFact? authorization = null,
        string? adapterContractVersion = null, string? directHumanGuidance = null)
    {
        var (run, attempt) = (Guid.NewGuid(), Guid.NewGuid());
        var partial = _store.GetPartialPath(run, attempt, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
        await File.WriteAllTextAsync(partial, manifest, new UTF8Encoding(false));
        var sealedFile = await _store.SealAsync(run, attempt, ArtifactPurpose.AgentContextManifest, CancellationToken.None);
        Assert.NotNull(sealedFile);
        return await DeliverAsync(
            new Subject(
                contract, provider, run, attempt, AdapterContractVersion: adapterContractVersion, DirectHumanGuidance: directHumanGuidance), sealedFile!.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash,
            _scene.Repository, _store, _launch, authorization);
    }

    private static bool IsDirectlyGuided(string form) =>
        form.Contains("guided", StringComparison.Ordinal) || form.Contains("direct-guidance", StringComparison.Ordinal);

    /// <summary>The direct-guidance forms exist only under the Claude mutation contracts' second version; the others keep the first.</summary>
    private static string? AdapterVersion(string form, AgentResponseContract contract) =>
        IsDirectlyGuided(form)
            ? contract == AgentResponseContract.ImplementationReport
                ? ClaudeMutationAdapterContract.ImplementationV2
                : ClaudeMutationAdapterContract.ReviewCorrectionV2
            : null;

    private static int Occurrences(string text, string value) => text.Split(value).Length - 1;
}
