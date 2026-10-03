using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RequestDiagnosisCorrection;

/// <summary>
/// Claims one correction attempt (the existing review-correction contract and Claude correction adapter) for a completed,
/// exactly applicable verification diagnosis that recorded findings, or records the diagnosis's one durable human escalation
/// at exhaustion of the shared correction allowance (ADR-0018). Optional short advisory direct human guidance (ADR-0015, extended by
/// ADR-0019) is accepted only within that allowance; at exhaustion a request carrying guidance is refused and creates nothing.
/// The ordinary review-correction endpoint keeps its review-source contract; this one accepts no authorization.
/// </summary>
public sealed class RequestDiagnosisCorrectionEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = 8 * 1024;

    [HttpPost("{runId:guid}/agent-attempts/verification-diagnosis/correction")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    public async Task<ActionResult<RequestDiagnosisCorrectionResponse>> RequestDiagnosisCorrection(
        [FromRoute] Guid runId, [FromBody] RequestDiagnosisCorrectionRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new CreateDiagnosisCorrectionAttemptCommand(runId, request.VerificationDiagnosisAttemptId, request.Guidance), cancellationToken);
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
