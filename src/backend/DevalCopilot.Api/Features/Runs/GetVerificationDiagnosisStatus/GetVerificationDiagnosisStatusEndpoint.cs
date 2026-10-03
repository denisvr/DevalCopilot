using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.Contracts;
using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;
using DevalCopilot.Application.Features.Runs.Queries.GetVerificationDiagnosisStatus;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetVerificationDiagnosisStatus;

/// <summary>
/// Bounded status of the most recent verification diagnosis on a run, with its findings count, escalation, correction, and
/// budget facts. An unknown run is a safe 404; an existing run with no diagnosis is a real 200 body with
/// <c>hasAttempt: false</c>. Never exposes a local path, prompt, transcript, verification output, credential, or raw provider
/// payload.
/// </summary>
public sealed class GetVerificationDiagnosisStatusEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/agent-attempts/verification-diagnosis")]
    public async Task<ActionResult<VerificationDiagnosisStatusResponse>> GetVerificationDiagnosisStatus(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetVerificationDiagnosisStatusQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new VerificationDiagnosisStatusResponse(
            value.HasAttempt,
            value.AttemptId,
            value.AttemptNumber,
            value.ExecutionReportMessageId,
            value.Status?.ToString(),
            value.Outcome?.ToString(),
            value.ClaimedAtUtc,
            value.DispatchedAtUtc,
            value.CompletedAtUtc,
            value.Artifacts
                .Select(artifact => new AgentAttemptArtifactMetadataResponse(
                    artifact.Purpose.ToString(), artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome.ToString()))
                .ToArray(),
            value.Verification
                .Select(member => new VerificationDiagnosisMemberResponse(
                    member.Position, member.CommandName, member.ExecutionNumber, member.Status, member.ExitCode))
                .ToArray(),
            value.FindingCount,
            value.DiagnosisEscalationMessageId,
            value.CorrectionApplicable,
            value.CorrectionAttemptId,
            value.CorrectionAttemptNumber,
            value.CorrectionStatus?.ToString(),
            value.CorrectionOutcome?.ToString(),
            value.ReviewableExecutionReportMessageId,
            value.MaximumReviewCorrectionAttempts,
            value.ReviewCorrectionAttemptsUsed,
            value.CorrectionBudgetExhausted,
            value.CorrectionEscalationId,
            value.CorrectionEscalationMessageId,
            value.DiagnosableExecutionReportMessageId,
            value.DiagnosisUnavailableCode,
            AgentProcessExecutionResponse.FromDomain(value.HasAttempt, value.ProcessExecution, value.Timeout),
            AgentTokenUsageResponse.FromDomain(value.HasAttempt, value.TokenUsage),
            value.ConfiguredCommandSandbox,
            value.ConfiguredRolloutPersistence,
            DirectHumanGuidanceResponse.FromDomain(value.CorrectionDirectGuidance)));
    }
}
