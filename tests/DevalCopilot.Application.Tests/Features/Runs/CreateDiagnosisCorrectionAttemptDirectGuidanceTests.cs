using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Optional direct human guidance on the explicit diagnosis-origin correction request (ADR-0019, extending ADR-0015 and
/// ADR-0018): the exact normalized text is snapshotted on the claimed attempt and sealed once into the existing manifest
/// envelope, only within the shared correction allowance, with every source, applicability, budget, lease and checkpoint gate
/// unchanged. At exhaustion a guided request is refused whole while an unguided one keeps its idempotent escalation. Real
/// file-backed SQLite throughout.
/// </summary>
public sealed class CreateDiagnosisCorrectionAttemptDirectGuidanceTests : IAsyncLifetime
{
    private const string Guidance = "SENTINEL-DIAG-GUIDE Keep the fix inside the existing helper.";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static string Code<T>(Devalente.Shared.Results.Result<T> result) => Assert.Single(result.Errors).Code;

    private async Task<(DiagnosisTestScene Scene, Attempt Diagnosis)> SceneAsync(int priorCorrections = 0)
    {
        var scene = await CreateAsync(
            _fixture, [Spec.Passed(), Spec.Failed(stdout: "SENTINEL-RAW-STDOUT", stderr: "SENTINEL-RAW-STDERR")],
            maximumAgentInvocationTime: TimeSpan.FromHours(48));
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, 2)).Attempt;
        await scene.AddCorrectionHistoryAsync(priorCorrections);
        return (scene, diagnosis);
    }

    private async Task<string?> ReadDirectGuidanceAsync(Guid attemptId)
    {
        await using var context = _fixture.CreateContext();
        return await context.Database
            .SqlQuery<string?>($"SELECT AgentDirectHumanGuidance AS Value FROM attempts WHERE Id = {attemptId}")
            .SingleAsync();
    }

    private async Task AssertNothingClaimedAsync(DiagnosisTestScene scene, (int Attempts, int Artifacts, int Inputs, int Evidence, int Messages, int Events) before)
    {
        Assert.Equal(before, await scene.CountsAsync());
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.DiagnosisCorrectionEscalations);
        Assert.Empty(verify.ReviewCorrectionEscalations);
        Assert.Empty(verify.ReviewCorrectionAuthorizations);
    }

    // ---- Within the allowance ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_guided_claim_snapshots_the_exact_normalized_text_and_seals_it_once_before_the_untrusted_evidence()
    {
        var (scene, diagnosis) = await SceneAsync();

        var result = await scene.ClaimCorrectionAsync(
            diagnosis.Id, guidance: $"  {Guidance.Replace("Keep", "Keep\r\n", StringComparison.Ordinal)} \r\n");

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        var created = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        var expected = Guidance.Replace("Keep", "Keep\n", StringComparison.Ordinal);
        Assert.Equal(expected, await ReadDirectGuidanceAsync(created.AttemptId));

        var manifestText = scene.Store.ReadManifest(scene.Run.Id, created.AttemptId);
        Assert.True(DirectHumanGuidanceManifest.Agrees(manifestText, expected));
        Assert.Equal(1, manifestText.Split("SENTINEL-DIAG-GUIDE").Length - 1);
        using var manifest = JsonDocument.Parse(manifestText);
        var names = manifest.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.True(Array.IndexOf(names, "sourceNotice") < Array.IndexOf(names, "directHumanGuidanceBoundary"));
        Assert.Equal(Array.IndexOf(names, "directHumanGuidanceBoundary") + 1, Array.IndexOf(names, "directHumanGuidance"));
        Assert.True(Array.IndexOf(names, "directHumanGuidance") < Array.IndexOf(names, "untrustedEvidenceBoundary"));
        Assert.False(manifest.RootElement.TryGetProperty("humanGuidance", out _));
        Assert.Equal(
            "These findings came from an explicit diagnosis of a failed local verification of this implementation, not from a " +
            "code review. Correct only what the findings require. The failing verification output is not included and you must " +
            "not run verification yourself. Do not change verification commands, tools, permissions, or the scope of the plan.",
            manifest.RootElement.GetProperty("sourceNotice").GetString());
        Assert.DoesNotContain("SENTINEL-RAW-STDOUT", manifestText, StringComparison.Ordinal);

        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == created.AttemptId);
        Assert.Equal(AgentResponseContract.ReviewCorrection, attempt.AgentResponseContract);
        Assert.Equal("claude-review-correction-v2", attempt.AgentAdapterContractVersion);
        Assert.Equal(AgentPermissionProfile.WorkspaceEditOnly, attempt.AgentPermissionProfile);
        Assert.Equal(DirectHumanGuidanceEvidence.Provided, attempt.GetDirectHumanGuidanceEvidence());
        var findings = await verify.CollaborationMessages.AsNoTracking()
            .Where(m => m.AttemptId == diagnosis.Id && m.Type == CollaborationMessageType.ReviewFinding)
            .OrderBy(m => m.Sequence).Select(m => m.Id).ToListAsync();
        var inputs = await verify.AttemptInputMessages.Where(i => i.AttemptId == created.AttemptId).OrderBy(i => i.Sequence).ToListAsync();
        Assert.Equal([scene.ReportId, .. findings], inputs.Select(i => i.CollaborationMessageId));
        Assert.Empty(verify.DiagnosisCorrectionEscalations);
        Assert.Empty(verify.ReviewCorrectionAuthorizations);
        Assert.Empty(verify.CollaborationMessages.Where(m => m.Type == CollaborationMessageType.HumanInstruction));
    }

    [Fact]
    public async Task Guidance_changes_nothing_else_in_the_manifest_and_grants_no_extra_correction()
    {
        var (scene, diagnosis) = await SceneAsync();
        var guided = await scene.ClaimCorrectionAsync(diagnosis.Id, guidance: Guidance);
        var guidedAttempt = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(guided.Value).AttemptId;
        var (plainScene, plainDiagnosis) = await SceneAsync();
        var plain = await plainScene.ClaimCorrectionAsync(plainDiagnosis.Id);
        var plainAttempt = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(plain.Value).AttemptId;

        using var guidedManifest = JsonDocument.Parse(scene.Store.ReadManifest(scene.Run.Id, guidedAttempt));
        using var plainManifest = JsonDocument.Parse(plainScene.Store.ReadManifest(plainScene.Run.Id, plainAttempt));

        var guidedOnly = guidedManifest.RootElement.EnumerateObject().Select(p => p.Name).Except(plainManifest.RootElement.EnumerateObject().Select(p => p.Name)).ToArray();
        Assert.Equal(["directHumanGuidanceBoundary", "directHumanGuidance"], guidedOnly);
        foreach (var name in new[] { "protocolVersion", "expectedResponseContract", "instruction", "expectedOutputSchema", "sourceNotice", "untrustedEvidenceBoundary" })
        {
            Assert.Equal(plainManifest.RootElement.GetProperty(name).GetRawText(), guidedManifest.RootElement.GetProperty(name).GetRawText());
        }

        await using var verify = _fixture.CreateContext();
        Assert.Equal(1, await verify.Attempts.CountAsync(a => a.RunId == scene.Run.Id && a.AgentResponseContract == AgentResponseContract.ReviewCorrection));
        Assert.Empty(verify.ReviewCorrectionAuthorizations);
    }

    [Fact]
    public async Task An_unguided_and_a_null_guidance_claim_record_no_snapshot_and_seal_neither_member()
    {
        var (scene, diagnosis) = await SceneAsync();

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, guidance: null);

        var created = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        Assert.Null(await ReadDirectGuidanceAsync(created.AttemptId));
        Assert.True(DirectHumanGuidanceManifest.Agrees(scene.Store.ReadManifest(scene.Run.Id, created.AttemptId), null));
        await using var verify = _fixture.CreateContext();
        Assert.Equal(
            DirectHumanGuidanceEvidence.NotRecorded,
            (await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == created.AttemptId)).GetDirectHumanGuidanceEvidence());
        Assert.Equal(new CreateDiagnosisCorrectionAttemptCommand(scene.Run.Id, diagnosis.Id), new CreateDiagnosisCorrectionAttemptCommand(scene.Run.Id, diagnosis.Id, null));
    }

    public static IEnumerable<object[]> InvalidGuidance()
    {
        yield return [""];
        yield return ["  \r\n "];
        yield return ["tab\there SENTINEL-INVALID"];
        yield return ["bell\u0007 SENTINEL-INVALID"];
        yield return ["SENTINEL-INVALID the password is x"];
        yield return [new string('x', 601) + " SENTINEL-INVALID"];
        yield return [new string(['a', '\ud800', 'b'])];
    }

    [Theory]
    [MemberData(nameof(InvalidGuidance))]
    public async Task Invalid_guidance_is_refused_before_any_read_or_external_work_and_is_never_echoed(string guidance)
    {
        var (scene, diagnosis) = await SceneAsync();
        var before = await scene.CountsAsync();
        var reader = scene.Reader();

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, reader: reader, guidance: guidance);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempts.direct_guidance_invalid", error.Code);
        Assert.DoesNotContain("SENTINEL", error.Description, StringComparison.Ordinal);
        Assert.Equal(0, reader.Calls);
        Assert.Equal(0, scene.Store.SealCount);
        Assert.Empty(scene.Store.DeletedSealedFiles);
        await AssertNothingClaimedAsync(scene, before);
    }

    [Fact]
    public async Task The_validator_rejects_invalid_guidance_with_a_fixed_code_and_accepts_null_and_valid_text()
    {
        var validator = new CreateDiagnosisCorrectionAttemptCommandValidator();

        Assert.True((await validator.ValidateAsync(new CreateDiagnosisCorrectionAttemptCommand(Guid.NewGuid(), Guid.NewGuid()))).IsValid);
        Assert.True((await validator.ValidateAsync(new CreateDiagnosisCorrectionAttemptCommand(Guid.NewGuid(), Guid.NewGuid(), Guidance))).IsValid);
        var invalid = await validator.ValidateAsync(new CreateDiagnosisCorrectionAttemptCommand(Guid.NewGuid(), Guid.NewGuid(), " "));
        Assert.Equal("agent_attempts.direct_guidance_invalid", Assert.Single(invalid.Errors).ErrorCode);
        Assert.DoesNotContain("SENTINEL", Assert.Single(invalid.Errors).ErrorMessage, StringComparison.Ordinal);
    }

    // ---- Every existing gate still decides first ---------------------------------------------------------------------------

    [Fact]
    public async Task A_guided_request_for_a_source_that_does_not_apply_gets_the_source_refusal_not_the_guidance_one()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var escalated = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisEscalated)).Attempt;
        var before = await scene.CountsAsync();

        var unknown = await scene.ClaimCorrectionAsync(Guid.NewGuid(), guidance: Guidance);
        var notFindings = await scene.ClaimCorrectionAsync(escalated.Id, guidance: Guidance);

        Assert.Equal(CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode, Code(unknown));
        Assert.Equal(CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode, Code(notFindings));
        await AssertNothingClaimedAsync(scene, before);
    }

    [Fact]
    public async Task A_guided_request_never_bypasses_the_run_wide_agent_claim_budget()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], maximumAgentAttempts: 4);
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, 2)).Attempt;
        await scene.AddCorrectionHistoryAsync(2);
        var before = await scene.CountsAsync();

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, guidance: Guidance);

        Assert.Equal("agent_attempts.budget_exhausted", Code(result));
        await AssertNothingClaimedAsync(scene, before);
    }

    [Fact]
    public async Task A_guided_claim_that_loses_a_race_to_a_competing_correction_is_refused_and_removes_its_manifest()
    {
        var (scene, diagnosis) = await SceneAsync();
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = async _ =>
            {
                var winner = await scene.ClaimCorrectionAsync(diagnosis.Id);
                Assert.True(winner.IsSuccess, winner.IsFailure ? Code(winner) : null);
            },
        };

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, faulting, store: scene.Store, guidance: Guidance);

        Assert.Equal("attempts.run_has_active_attempt", Code(result));
        var sealedAttemptId = Assert.Single(scene.Store.SealedAttemptIds, id => scene.Store.DeletedSealedFiles.Contains((scene.Run.Id, id, ArtifactPurpose.AgentContextManifest)));
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.Id == sealedAttemptId));
        Assert.Equal(1, await verify.Attempts.CountAsync(a => a.AgentResponseContract == AgentResponseContract.ReviewCorrection));
    }

    // ---- Exhaustion of the shared allowance ------------------------------------------------------------------------------

    [Fact]
    public async Task At_exhaustion_before_sealing_a_guided_request_is_refused_whole_with_nothing_created_sealed_or_escalated()
    {
        var (scene, diagnosis) = await SceneAsync(priorCorrections: 2);
        var before = await scene.CountsAsync();

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, guidance: Guidance);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempts.direct_guidance_unavailable", error.Code);
        Assert.DoesNotContain("SENTINEL", error.Description, StringComparison.Ordinal);
        Assert.Equal(0, scene.Store.SealCount);
        await AssertNothingClaimedAsync(scene, before);
    }

    [Fact]
    public async Task At_exhaustion_an_unguided_request_keeps_its_one_idempotent_escalation_and_a_guided_request_never_disturbs_it()
    {
        var (scene, diagnosis) = await SceneAsync(priorCorrections: 2);

        var guidedFirst = await scene.ClaimCorrectionAsync(diagnosis.Id, guidance: Guidance);
        var plain = await scene.ClaimCorrectionAsync(diagnosis.Id);
        var guidedAfter = await scene.ClaimCorrectionAsync(diagnosis.Id, guidance: Guidance);
        var plainAgain = await scene.ClaimCorrectionAsync(diagnosis.Id);

        Assert.Equal("agent_attempts.direct_guidance_unavailable", Code(guidedFirst));
        var escalated = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(plain.Value);
        Assert.Equal("agent_attempts.direct_guidance_unavailable", Code(guidedAfter));
        Assert.Equal(escalated.EscalationId, Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(plainAgain.Value).EscalationId);
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.DiagnosisCorrectionEscalations.ToListAsync());
        Assert.Equal(2, await verify.Attempts.CountAsync(a => a.AgentResponseContract == AgentResponseContract.ReviewCorrection));
    }

    [Fact]
    public async Task A_guided_request_at_exhaustion_does_not_consume_or_read_an_available_ordinary_grant()
    {
        var (scene, diagnosis) = await SceneAsync(priorCorrections: 2);
        var before = await scene.CountsAsync();

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, guidance: Guidance);

        Assert.Equal("agent_attempts.direct_guidance_unavailable", Code(result));
        await AssertNothingClaimedAsync(scene, before);
    }

    [Fact]
    public async Task When_the_allowance_is_spent_exactly_at_the_locked_claim_seam_a_guided_request_is_refused_and_the_orphan_manifest_removed()
    {
        var (scene, diagnosis) = await SceneAsync(priorCorrections: 0);
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = _ => scene.AddCorrectionHistoryAsync(2),
        };
        var before = await scene.CountsAsync();

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, faulting, store: scene.Store, guidance: Guidance);

        Assert.Equal("agent_attempts.direct_guidance_unavailable", Code(result));
        var sealedAttemptId = Assert.Single(scene.Store.SealedAttemptIds);
        Assert.Contains((scene.Run.Id, sealedAttemptId, ArtifactPurpose.AgentContextManifest), scene.Store.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.Id == sealedAttemptId));
        Assert.Empty(verify.AttemptInputMessages.Where(i => i.AttemptId == sealedAttemptId));
        Assert.Empty(verify.Artifacts.Where(a => a.AttemptId == sealedAttemptId));
        Assert.Empty(verify.DiagnosisCorrectionEscalations);
        Assert.Equal(before.Messages, await verify.CollaborationMessages.CountAsync());
        Assert.Equal(before.Events, await verify.Events.CountAsync());
        Assert.Empty(verify.ReviewCorrectionAuthorizations);
    }

    [Fact]
    public async Task When_the_allowance_is_spent_exactly_at_the_locked_claim_seam_an_unguided_request_still_records_the_escalation()
    {
        var (scene, diagnosis) = await SceneAsync(priorCorrections: 0);
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = _ => scene.AddCorrectionHistoryAsync(2),
        };

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, faulting, store: scene.Store);

        Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(result.Value);
        var sealedAttemptId = Assert.Single(scene.Store.SealedAttemptIds);
        Assert.Contains((scene.Run.Id, sealedAttemptId, ArtifactPurpose.AgentContextManifest), scene.Store.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.DiagnosisCorrectionEscalations.ToListAsync());
    }

    [Fact]
    public async Task The_ordinary_review_correction_command_and_its_guidance_are_unaffected()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var review = (await scene.AddOrdinaryChangesRequestedReviewAsync()).Attempt;

        var result = await scene.OrdinaryCorrectionHandler(_fixture.CreateContext()).HandleAsync(
            new CreateReviewCorrectionAttemptCommand(scene.Run.Id, review.Id, Guidance), CancellationToken.None);

        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        Assert.True(DirectHumanGuidanceManifest.Agrees(scene.Store.ReadManifest(scene.Run.Id, created.AttemptId), Guidance));
    }
}
