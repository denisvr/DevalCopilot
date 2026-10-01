using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Implemented-plan identity through review and correction (ADR-0017): every newly claimed CodeReviewer attempt judges the
/// exact Proposal the initial implementation consumed as its sequence-zero input, while the Planner root stays the historical
/// lineage and correction-reply identity. Real file-backed SQLite and the real claim handler; the root and the revised plan
/// have substantively different summaries and steps, so identifiers and plan content are both asserted.
/// </summary>
public sealed class CreateCodeReviewImplementedPlanTests : IAsyncLifetime
{
    private const string RootSteps = "Add the table then the query";
    private const string RevisedStepsMarker = "Revised steps unique to scope";
    private const string RootSummary = "Add the ledger table and its query.";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed record Setup(RepairTestScene Scene, ImplementationScene Implementation, VerificationScene Verification, PlanForm Form);

    private async Task<Setup> SeedAsync(PlanForm form, bool corrected)
    {
        var scene = await CreateAsync(_fixture);
        var implementation = corrected ? scene.AddCorrectedImplementation(form) : scene.AddInitialImplementation(form);
        var verification = await scene.AddPassedVerificationAsync(implementation, commandCount: 1);
        await scene.SaveAsync();
        return new Setup(scene, implementation, verification, form);
    }

    private CreateCodeReviewAttemptCommandHandler NewHandler(
        IDevalCopilotDbContext db, Setup setup, RepairArtifactStore store, RepairEvidenceReader? reader = null) =>
        new(db, reader ?? RepairEvidenceReader.Matching(setup.Implementation.ReviewFingerprint), store,
            new FixedTimeProvider(Now), new AttemptDurabilityProbe(_fixture.Options));

    private static string Code<T>(Result<T> result) => Assert.Single(result.Errors).Code;

    private static string Normalized(string json) => JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json));

    /// <summary>The sealed manifest names the implemented Proposal — identifier, summary and structured content — and,
    /// for a revision, none of the superseded root's text.</summary>
    private static void AssertImplementedPlan(string manifestText, Setup setup)
    {
        using var manifest = JsonDocument.Parse(manifestText);
        var plan = manifest.RootElement.GetProperty("resolvedPlan");
        var implemented = setup.Implementation.ResolvedPlan;
        Assert.Equal(implemented.Id, plan.GetProperty("messageId").GetGuid());
        Assert.Equal(implemented.Summary, plan.GetProperty("summary").GetString());
        Assert.Equal(Normalized(implemented.StructuredContentJson), Normalized(plan.GetProperty("structuredContent").GetRawText()));
        if (setup.Form == PlanForm.AcceptedRoot)
        {
            Assert.Equal(setup.Implementation.OriginalPlan.Id, implemented.Id);
            Assert.Contains(RootSteps, manifestText, StringComparison.Ordinal);
            return;
        }

        Assert.NotEqual(setup.Implementation.OriginalPlan.Id, plan.GetProperty("messageId").GetGuid());
        Assert.Contains(RevisedStepsMarker, plan.GetProperty("structuredContent").GetProperty("implementationSteps").GetString());
        Assert.DoesNotContain(RootSteps, manifestText, StringComparison.Ordinal);
        Assert.DoesNotContain(RootSummary, manifestText, StringComparison.Ordinal);
        Assert.DoesNotContain(setup.Implementation.OriginalPlan.Id.ToString(), manifestText, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<string, bool> Cases => new()
    {
        { "AcceptedRoot", false },
        { "FirstRevision", false },
        { "AcceptedFirstRevision", false },
        { "AcceptedRoot", true },
        { "FirstRevision", true },
        { "AcceptedFirstRevision", true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task An_initial_review_or_a_re_review_seals_the_implemented_plan(string formName, bool corrected)
    {
        var setup = await SeedAsync(Enum.Parse<PlanForm>(formName), corrected);
        var store = new RepairArtifactStore();

        var result = await NewHandler(setup.Scene.Db, setup, store).HandleAsync(
            new CreateCodeReviewAttemptCommand(setup.Scene.Run.Id, setup.Implementation.ExecutionReport.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        var manifestText = store.ReadManifest(setup.Scene.Run.Id, result.Value.AttemptId);
        AssertImplementedPlan(manifestText, setup);
        using var manifest = JsonDocument.Parse(manifestText);
        Assert.Equal(corrected, manifest.RootElement.TryGetProperty("correctionEvidence", out _));
        await using var verify = _fixture.CreateContext();
        var input = Assert.Single(verify.AttemptInputMessages.Where(m => m.AttemptId == result.Value.AttemptId));
        Assert.Equal(setup.Implementation.ExecutionReport.Id, input.CollaborationMessageId);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_newly_requested_format_repair_of_an_older_failed_source_uses_the_implemented_plan(string formName, bool corrected)
    {
        var setup = await SeedAsync(Enum.Parse<PlanForm>(formName), corrected);
        var source = setup.Scene.AddInvalidCodeReview(setup.Implementation, setup.Verification);
        await setup.Scene.SaveAsync();
        var store = new RepairArtifactStore();

        var result = await NewHandler(setup.Scene.Db, setup, store).HandleAsync(
            CreateCodeReviewAttemptCommand.ForRepair(setup.Scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        var manifestText = store.ReadManifest(setup.Scene.Run.Id, result.Value.AttemptId);
        AssertImplementedPlan(manifestText, setup);
        using var manifest = JsonDocument.Parse(manifestText);
        Assert.Equal(CodeReviewContextManifestBuilder.FormatRepairNotice, manifest.RootElement.GetProperty("formatRepairNotice").GetString());
        await using var verify = _fixture.CreateContext();
        Assert.Equal(source.Id, (await verify.Attempts.SingleAsync(a => a.Id == result.Value.AttemptId)).AgentRepairSourceAttemptId);
        var input = Assert.Single(verify.AttemptInputMessages.Where(m => m.AttemptId == result.Value.AttemptId));
        Assert.Equal(setup.Implementation.ExecutionReport.Id, input.CollaborationMessageId);
    }

    [Theory]
    [InlineData("FirstRevision")]
    [InlineData("AcceptedFirstRevision")]
    public async Task The_chain_keeps_the_root_as_lineage_identity_and_the_revision_as_the_implemented_plan(string formName)
    {
        foreach (var corrected in new[] { false, true })
        {
            var setup = await SeedAsync(Enum.Parse<PlanForm>(formName), corrected);
            await using var context = _fixture.CreateContext();

            var chain = await ImplementerExecutionReportEligibility.ResolveAsync(
                context, setup.Implementation.ExecutionReport, setup.Scene.Run.Id, setup.Scene.Workspace.Id,
                setup.Implementation.ReviewCheckpoint.Id, CancellationToken.None);

            Assert.NotNull(chain);
            Assert.Equal(setup.Implementation.OriginalPlan.Id, chain.OriginalProposal.Id);
            Assert.Equal(setup.Implementation.ResolvedPlan.Id, chain.ImplementedPlan.Id);
            Assert.NotEqual(chain.OriginalProposal.Id, chain.ImplementedPlan.Id);
            Assert.NotEqual(chain.OriginalProposal.Summary, chain.ImplementedPlan.Summary);
            Assert.NotEqual(chain.OriginalProposal.StructuredContentJson, chain.ImplementedPlan.StructuredContentJson);
        }
    }

    // ---- Refusals: authority comes from the whole validated input chain, never from an unrelated reply -----------------

    public static TheoryData<string, bool> Tampers => new()
    {
        { "initial-reply-to-root", false },
        { "initial-reply-to-unrelated-root", false },
        { "initial-reply-to-unrelated-revision", false },
        { "missing-first-input", false },
        { "foreign-first-input", false },
        { "initial-reply-to-root", true },
        { "correction-replies-to-revision", true },
        { "correction-replies-to-unrelated-root", true },
    };

    private async Task ApplyTamperAsync(Setup setup, string tamper)
    {
        var scene = setup.Scene;
        var unrelatedRoot = scene.Lineage.AddRoot().Proposal;
        var unrelatedRevision = scene.Lineage.AddChallengedRound(scene.Lineage.AddRoot().Proposal).Resolution.RevisedProposal;
        await scene.SaveAsync();

        await using var ctx = _fixture.CreateContext();
        var report = setup.Implementation.ExecutionReport;
        var initialAttempt = await ctx.Attempts.AsNoTracking().SingleAsync(a =>
            a.RunId == scene.Run.Id && a.AgentRole == AgentRole.Implementer && a.AgentResponseContract == AgentResponseContract.ImplementationReport);
        var initialReport = await ctx.CollaborationMessages.AsNoTracking().SingleAsync(m =>
            m.AttemptId == initialAttempt.Id && m.Type == CollaborationMessageType.ExecutionReport);

        async Task Reply(Guid messageId, Guid to) => await ctx.CollaborationMessages.Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.InReplyToMessageId, (Guid?)to));

        switch (tamper)
        {
            case "initial-reply-to-root":
                await Reply(initialReport.Id, setup.Implementation.OriginalPlan.Id);
                break;
            case "initial-reply-to-unrelated-root":
                await Reply(initialReport.Id, unrelatedRoot.Id);
                break;
            case "initial-reply-to-unrelated-revision":
                await Reply(initialReport.Id, unrelatedRevision.Id);
                break;
            case "missing-first-input":
                await ctx.AttemptInputMessages.Where(i => i.AttemptId == initialAttempt.Id && i.Sequence == 0).ExecuteDeleteAsync();
                break;
            case "foreign-first-input":
                await ctx.AttemptInputMessages.Where(i => i.AttemptId == initialAttempt.Id && i.Sequence == 0)
                    .ExecuteUpdateAsync(set => set.SetProperty(i => i.CollaborationMessageId, unrelatedRevision.Id));
                break;
            case "correction-replies-to-revision":
                await Reply(report.Id, setup.Implementation.ResolvedPlan.Id);
                break;
            default:
                await Reply(report.Id, unrelatedRoot.Id);
                break;
        }

        scene.Detach();
    }

    [Theory]
    [MemberData(nameof(Tampers))]
    public async Task A_report_whose_reply_or_inputs_do_not_match_the_implemented_plan_is_not_a_valid_chain(string tamper, bool corrected)
    {
        var setup = await SeedAsync(PlanForm.FirstRevision, corrected);
        await ApplyTamperAsync(setup, tamper);
        await using var context = _fixture.CreateContext();
        var report = await context.CollaborationMessages.AsNoTracking().SingleAsync(m => m.Id == setup.Implementation.ExecutionReport.Id);

        var chain = await ImplementerExecutionReportEligibility.ResolveAsync(
            context, report, setup.Scene.Run.Id, setup.Scene.Workspace.Id, setup.Implementation.ReviewCheckpoint.Id, CancellationToken.None);

        Assert.Null(chain);
    }

    [Theory]
    [MemberData(nameof(Tampers))]
    public async Task A_review_claim_over_a_broken_plan_chain_is_refused_without_an_attempt_reservation_or_artifact(string tamper, bool corrected)
    {
        var setup = await SeedAsync(PlanForm.FirstRevision, corrected);
        await ApplyTamperAsync(setup, tamper);
        var attemptsBefore = await setup.Scene.AttemptCountAsync();
        var store = new RepairArtifactStore();
        await using var context = _fixture.CreateContext();

        var result = await NewHandler(context, setup, store).HandleAsync(
            new CreateCodeReviewAttemptCommand(setup.Scene.Run.Id, setup.Implementation.ExecutionReport.Id), CancellationToken.None);

        Assert.True(result.IsFailure, tamper);
        Assert.Equal("agent_attempts.implementer_attempt_not_valid", Code(result));
        Assert.Equal(0, store.SealCount);
        Assert.Equal(attemptsBefore, await setup.Scene.AttemptCountAsync());
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Artifacts.Where(a => a.Purpose == ArtifactPurpose.AgentContextManifest
            && verify.Attempts.Any(x => x.Id == a.AttemptId && x.AgentRole == AgentRole.CodeReviewer)));
    }

    // ---- The repair commit seam: an authority change committed immediately before BEGIN ------------------------------

    [Theory]
    [InlineData("report-reply-to-root")]
    [InlineData("first-input-removed")]
    public async Task Plan_authority_changed_at_the_repair_claim_seam_is_refused_despite_the_populated_context(string change)
    {
        var setup = await SeedAsync(PlanForm.FirstRevision, corrected: false);
        var source = setup.Scene.AddInvalidCodeReview(setup.Implementation, setup.Verification);
        await setup.Scene.SaveAsync();
        var store = new RepairArtifactStore();
        var reader = RepairEvidenceReader.Matching(setup.Implementation.ReviewFingerprint);
        var faulting = new FaultInjectingDbContext(setup.Scene.Db)
        {
            BeforeBeginTransaction = async _ =>
            {
                await using var racing = _fixture.CreateContext();
                var attemptId = setup.Implementation.ExecutionReport.AttemptId!.Value;
                if (change == "report-reply-to-root")
                {
                    await racing.CollaborationMessages.Where(m => m.Id == setup.Implementation.ExecutionReport.Id)
                        .ExecuteUpdateAsync(set => set.SetProperty(m => m.InReplyToMessageId, (Guid?)setup.Implementation.OriginalPlan.Id));
                }
                else
                {
                    await racing.AttemptInputMessages.Where(i => i.AttemptId == attemptId && i.Sequence == 0).ExecuteDeleteAsync();
                }
            },
        };

        var result = await NewHandler(faulting, setup, store, reader).HandleAsync(
            CreateCodeReviewAttemptCommand.ForRepair(setup.Scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure, change);
        Assert.Equal("agent_attempts.implementer_attempt_not_valid", Code(result));
        var deleted = Assert.Single(store.DeletedSealedFiles);
        Assert.Equal(store.SealedAttemptIds[0], deleted.AttemptId);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.Id == deleted.AttemptId));
        Assert.Empty(verify.AttemptInputMessages.Where(m => m.AttemptId == deleted.AttemptId));
        Assert.Empty(verify.Artifacts.Where(a => a.AttemptId == deleted.AttemptId));
    }

    // ---- Manifest bytes: only the selected plan values change --------------------------------------------------------

    private static string BuildManifest(Guid planId, string summary, string content) => CodeReviewContextManifestBuilder.Build(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"), new string('b', 64), "The objective.", planId, summary, content,
        Guid.Parse("44444444-4444-4444-4444-444444444444"), "Report summary.", "{\"completedWork\":\"Done.\"}",
        [new CodeReviewContextManifestBuilder.VerificationEvidence("Backend tests", 1, "Passed", "Exited", 0)],
        [], null);

    [Fact]
    public void A_different_plan_changes_only_the_resolved_plan_values_and_the_member_order_is_pinned()
    {
        var rootId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var revisedId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var rootJson = "{\"scope\":\"Ledger\",\"implementationSteps\":\"Root steps\"}";
        var revisedJson = "{\"scope\":\"Revised\",\"implementationSteps\":\"Revised steps\"}";

        var root = BuildManifest(rootId, "Root summary.", rootJson);
        var revised = BuildManifest(revisedId, "Revised summary.", revisedJson);

        Assert.NotEqual(root, revised);
        Assert.Equal(
            root,
            revised.Replace(revisedId.ToString(), rootId.ToString()).Replace("Revised summary.", "Root summary.")
                .Replace("{\"scope\":\"Revised\",\"implementationSteps\":\"Revised steps\"}", rootJson));
        using var document = JsonDocument.Parse(root);
        Assert.Equal(
            new[]
            {
                "protocolVersion", "expectedResponseContract", "objective", "projectId", "gitWorkspaceId", "resultGitCheckpointId",
                "resultCheckpointFingerprintSha256", "instructionReferences", "instruction", "expectedOutputSchema",
                "untrustedEvidenceBoundary", "resolvedPlan", "executionReport", "verificationEvidence", "changeEvidence",
            },
            document.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            new[] { "messageId", "summary", "structuredContent" },
            document.RootElement.GetProperty("resolvedPlan").EnumerateObject().Select(p => p.Name));
    }
}
