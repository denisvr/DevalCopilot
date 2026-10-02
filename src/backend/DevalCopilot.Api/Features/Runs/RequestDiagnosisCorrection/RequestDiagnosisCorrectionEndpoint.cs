using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RequestDiagnosisCorrection;

/// <summary>
/// Claims one correction attempt (the existing review-correction contract and Claude correction adapter) for a completed,
/// exactly applicable verification diagnosis that recorded findings, or records the diagnosis's one durable human escalation
/// at exhaustion of the shared correction allowance (ADR-0018). The ordinary review-correction endpoint keeps its review-source
/// contract; this one accepts neither guidance nor an authorization.
/// </summary>
public sealed class RequestDiagnosisCorrectionEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/agent-attempts/verification-diagnosis/correction")]
    public async Task<ActionResult<RequestDiagnosisCorrectionResponse>> RequestDiagnosisCorrection(
        [FromRoute] Guid runId, [FromBody] RequestDiagnosisCorrectionRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new CreateDiagnosisCorrectionAttemptCommand(runId, request.VerificationDiagnosisAttemptId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        return result.Value switch
        {
            CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated created => Ok(
                new RequestDiagnosisCorrectionResponse("AttemptCreated", created.AttemptId, created.AttemptNumber, null, null, created.LatestEventSequence)),
            CreateDiagnosisCorrectionAttemptCommandResult.Escalated escalated => Ok(
                new RequestDiagnosisCorrectionResponse("Escalated", null, null, escalated.EscalationId, escalated.EscalationMessageId, escalated.LatestEventSequence)),
            _ => throw new InvalidOperationException("The correction result variant is not supported."),
        };
    }
}
