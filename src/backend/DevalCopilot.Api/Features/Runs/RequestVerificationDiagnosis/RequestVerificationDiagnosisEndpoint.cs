using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RequestVerificationDiagnosis;

/// <summary>
/// Creates one durable Codex verification-diagnosis attempt for the current failed local verification of exactly one explicit,
/// already-recorded, provider-observed Claude implementation execution report (ADR-0018). The host derives the complete
/// verification selection. Never accepts or exposes a prompt, local path, environment value, credential, command line, or raw
/// provider payload.
/// </summary>
public sealed class RequestVerificationDiagnosisEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/agent-attempts/verification-diagnosis")]
    public async Task<ActionResult<RequestVerificationDiagnosisResponse>> RequestVerificationDiagnosis(
        [FromRoute] Guid runId, [FromBody] RequestVerificationDiagnosisRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new CreateVerificationDiagnosisAttemptCommand(runId, request.ExecutionReportMessageId), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new RequestVerificationDiagnosisResponse(result.Value.AttemptId, result.Value.AttemptNumber));
    }
}
