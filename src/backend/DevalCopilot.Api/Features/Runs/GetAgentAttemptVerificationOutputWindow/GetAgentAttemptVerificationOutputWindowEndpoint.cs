using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationEvidence;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationOutputWindow;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationOutputWindow;

/// <summary>
/// Protected, read-only, bodyless sealed text window of one output stream of one recorded verification member of a CodeReviewer
/// attempt (ADR-0052), restricted to <c>stdout</c> and <c>stderr</c>. An unknown run, a missing, foreign or non-Agent attempt, an
/// execution that is not a proved member and an unsupported stream are one fixed 404 that repeats no value. An offset or byte cap
/// outside its bounds is a 400, never clamped. Every other outcome is a 200 with an explicit status; bytes are served only through the
/// artifact store's integrity-verifying sealed read.
/// </summary>
public sealed class GetAgentAttemptVerificationOutputWindowEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/agent-attempts/{attemptId:guid}/verification-evidence/{executionId:guid}/artifact-window/{stream}")]
    [ProducesResponseType<GetAgentAttemptVerificationOutputWindowResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetAgentAttemptVerificationOutputWindowResponse>> GetAgentAttemptVerificationOutputWindow(
        [FromRoute] Guid runId,
        [FromRoute] Guid attemptId,
        [FromRoute] Guid executionId,
        [FromRoute] string stream,
        [FromQuery, ModelBinder(typeof(VerificationOutputWindowOffsetModelBinder))] long? fromOffset,
        [FromQuery, ModelBinder(typeof(VerificationOutputWindowMaxBytesModelBinder))] int? maxBytes,
        CancellationToken cancellationToken)
    {
        var purpose = stream switch
        {
            "stdout" => VerificationOutputPurpose.StandardOutput,
            "stderr" => VerificationOutputPurpose.StandardError,
            _ => (VerificationOutputPurpose?)null,
        };
        if (purpose is not { } selected)
        {
            return problemDetails.CreateResponse(
                Result<GetAgentAttemptVerificationOutputWindowQueryResult>.Failure(AgentAttemptVerificationErrors.NotFound()), HttpContext);
        }

        var result = await mediator.SendAsync(
            new GetAgentAttemptVerificationOutputWindowQuery(
                runId,
                attemptId,
                executionId,
                selected,
                fromOffset ?? 0,
                maxBytes ?? GetAgentAttemptVerificationOutputWindowQuery.DefaultMaxBytes),
            cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetAgentAttemptVerificationOutputWindowResponse(
            value.RunId,
            value.AttemptId,
            value.ExecutionId,
            value.Stream == VerificationOutputPurpose.StandardOutput ? "stdout" : "stderr",
            value.Status.ToString(),
            value.Artifact is { } artifact ? GetAgentAttemptVerificationEvidenceEndpoint.MapStream(artifact) : null,
            value.Text,
            value.NextOffset,
            value.TotalLength));
    }
}
