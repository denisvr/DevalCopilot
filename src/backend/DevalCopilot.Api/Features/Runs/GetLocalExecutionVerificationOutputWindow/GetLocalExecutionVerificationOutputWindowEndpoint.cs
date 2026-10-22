using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationEvidence;
using DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationOutputWindow;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionVerificationOutputWindow;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionVerificationOutputWindow;

/// <summary>
/// Protected, read-only, bodyless sealed text window of one output stream of the verification a recorded step links (ADR-0061), restricted
/// to <c>stdout</c> and <c>stderr</c>. It accepts only the exact ordinal query keys <c>fromOffset</c> and <c>maxBytes</c>, each once, and
/// no body. An unknown run, a run without a sequence, a step outside it, a non-verification step and an unsupported stream are one fixed 404
/// that repeats no value. An offset or byte cap outside its bounds is a 400, never clamped. Every other outcome is a 200 with an explicit
/// status; bytes are served only through the artifact store's integrity-verifying sealed read, after the association was re-proved.
/// </summary>
public sealed class GetLocalExecutionVerificationOutputWindowEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/local-execution-sequence/inspection/steps/{stepId:guid}/artifact-window/{stream}")]
    [TypeFilter(typeof(LocalExecutionInspectionWindowGuardFilter))]
    [ProducesResponseType<GetLocalExecutionVerificationOutputWindowResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetLocalExecutionVerificationOutputWindowResponse>> GetLocalExecutionVerificationOutputWindow(
        [FromRoute] Guid runId,
        [FromRoute] Guid stepId,
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
                Result<GetLocalExecutionVerificationOutputWindowQueryResult>.Failure(LocalExecutionInspectionErrors.NotFound()), HttpContext);
        }

        var result = await mediator.SendAsync(
            new GetLocalExecutionVerificationOutputWindowQuery(
                runId,
                stepId,
                selected,
                fromOffset ?? 0,
                maxBytes ?? GetLocalExecutionVerificationOutputWindowQuery.DefaultMaxBytes),
            cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetLocalExecutionVerificationOutputWindowResponse(
            value.RunId,
            value.SequenceId,
            value.StepId,
            value.ExecutionId,
            value.Stream == VerificationOutputPurpose.StandardOutput ? "stdout" : "stderr",
            value.Status.ToString(),
            value.Artifact is { } artifact ? GetAgentAttemptVerificationEvidenceEndpoint.MapStream(artifact) : null,
            value.Text,
            value.NextOffset,
            value.TotalLength));
    }
}
