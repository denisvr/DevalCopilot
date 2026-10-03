using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptEvidence;
using DevalCopilot.Application.Features.Runs.Queries.GetVerificationDiagnosisStatus;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The read side of direct guidance on a diagnosis-origin correction (ADR-0019): the diagnosis status names the correction's own
/// <c>NotRecorded</c>, <c>Provided</c> or <c>Unknown</c> fact (null when no correction exists), kept apart from the diagnosis, the
/// escalation and any extra-claim authorization, and the attempt-evidence read agrees with it. Stored facts that disagree are
/// <c>Unknown</c> with no text; a diagnosis attempt (which is not a mutation contract) never carries the fact.
/// </summary>
public sealed class DiagnosisCorrectionDirectGuidanceProjectionTests : IAsyncLifetime
{
    private const string Guidance = "SENTINEL-PROJECTION Keep the fix inside the existing helper.";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(DiagnosisTestScene Scene, Attempt Diagnosis)> SceneAsync(int priorCorrections = 0)
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], maximumAgentInvocationTime: TimeSpan.FromHours(48));
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, 2)).Attempt;
        await scene.AddCorrectionHistoryAsync(priorCorrections);
        return (scene, diagnosis);
    }

    private async Task<VerificationDiagnosisStatusQueryResult> StatusAsync(Guid runId)
    {
        await using var db = _fixture.CreateContext();
        var result = await new GetVerificationDiagnosisStatusQueryHandler(db).HandleAsync(
            new GetVerificationDiagnosisStatusQuery(runId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    private async Task SetRawGuidanceAsync(Guid attemptId, string? value)
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlAsync($"UPDATE attempts SET AgentDirectHumanGuidance = {value} WHERE Id = {attemptId}");
    }

    [Fact]
    public async Task Without_a_correction_the_fact_is_null_for_a_diagnosis_with_and_without_findings_and_for_no_diagnosis()
    {
        var (scene, _) = await SceneAsync();
        var noDiagnosis = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);

        Assert.Null((await StatusAsync(scene.Run.Id)).CorrectionDirectGuidance);
        Assert.Null((await StatusAsync(noDiagnosis.Run.Id)).CorrectionDirectGuidance);
    }

    [Fact]
    public async Task A_guided_correction_projects_Provided_with_the_exact_text_and_the_diagnosis_itself_carries_none()
    {
        var (scene, diagnosis) = await SceneAsync();
        var claim = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(
            (await scene.ClaimCorrectionAsync(diagnosis.Id, guidance: Guidance)).Value);

        var status = await StatusAsync(scene.Run.Id);

        Assert.Equal(claim.AttemptId, status.CorrectionAttemptId);
        var fact = Assert.IsType<DirectHumanGuidanceFact>(status.CorrectionDirectGuidance);
        Assert.Equal(DirectHumanGuidanceEvidence.Provided, fact.Evidence);
        Assert.Equal(Guidance, fact.Text);
        Assert.Null(status.CorrectionEscalationId);
        Assert.Equal(AgentOutcome.DiagnosisFindingsRecorded, status.Outcome);
    }

    [Fact]
    public async Task An_unguided_correction_projects_NotRecorded_with_no_text()
    {
        var (scene, diagnosis) = await SceneAsync();
        await scene.ClaimCorrectionAsync(diagnosis.Id);

        var fact = Assert.IsType<DirectHumanGuidanceFact>((await StatusAsync(scene.Run.Id)).CorrectionDirectGuidance);

        Assert.Equal(DirectHumanGuidanceEvidence.NotRecorded, fact.Evidence);
        Assert.Null(fact.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" padded ")]
    [InlineData("line\r\nbreak")]
    [InlineData("the password is x")]
    public async Task Malformed_stored_text_projects_Unknown_with_no_text_and_never_throws(string stored)
    {
        var (scene, diagnosis) = await SceneAsync();
        var claim = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(
            (await scene.ClaimCorrectionAsync(diagnosis.Id, guidance: Guidance)).Value);
        await SetRawGuidanceAsync(claim.AttemptId, stored);

        var fact = Assert.IsType<DirectHumanGuidanceFact>((await StatusAsync(scene.Run.Id)).CorrectionDirectGuidance);

        Assert.Equal(DirectHumanGuidanceEvidence.Unknown, fact.Evidence);
        Assert.Null(fact.Text);
    }

    [Fact]
    public async Task Guidance_beside_an_incoherent_contract_version_projects_Unknown()
    {
        var (scene, diagnosis) = await SceneAsync();
        var claim = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(
            (await scene.ClaimCorrectionAsync(diagnosis.Id, guidance: Guidance)).Value);
        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlAsync($"UPDATE attempts SET AgentAdapterContractVersion = 'claude-review-correction-v1' WHERE Id = {claim.AttemptId}");
        }

        var fact = Assert.IsType<DirectHumanGuidanceFact>((await StatusAsync(scene.Run.Id)).CorrectionDirectGuidance);

        Assert.Equal(DirectHumanGuidanceEvidence.Unknown, fact.Evidence);
        Assert.Null(fact.Text);
    }

    [Fact]
    public async Task The_status_and_the_attempt_evidence_read_agree_on_the_correction_fact()
    {
        var (scene, diagnosis) = await SceneAsync();
        var claim = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(
            (await scene.ClaimCorrectionAsync(diagnosis.Id, guidance: Guidance)).Value);

        var status = await StatusAsync(scene.Run.Id);
        await using var db = _fixture.CreateContext();
        var evidence = await new GetAgentAttemptEvidenceQueryHandler(db).HandleAsync(
            new GetAgentAttemptEvidenceQuery(scene.Run.Id, claim.AttemptId), CancellationToken.None);

        Assert.True(evidence.IsSuccess, evidence.IsFailure ? evidence.Errors[0].Code : null);
        Assert.Equal(status.CorrectionDirectGuidance, evidence.Value.DirectGuidance);
    }

    [Fact]
    public async Task A_diagnosis_attempt_itself_never_carries_a_direct_guidance_fact()
    {
        var (scene, diagnosis) = await SceneAsync();
        await using var db = _fixture.CreateContext();

        var evidence = await new GetAgentAttemptEvidenceQueryHandler(db).HandleAsync(
            new GetAgentAttemptEvidenceQuery(scene.Run.Id, diagnosis.Id), CancellationToken.None);

        Assert.True(evidence.IsSuccess, evidence.IsFailure ? evidence.Errors[0].Code : null);
        Assert.Null(evidence.Value.DirectGuidance);
    }
}
