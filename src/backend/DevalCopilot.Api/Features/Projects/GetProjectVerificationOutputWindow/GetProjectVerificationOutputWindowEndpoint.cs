using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Api.Features.Projects.GetProjectVerificationHistory;
using DevalCopilot.Api.Features.Projects.GetProjectVerificationInspection;
using DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationOutputWindow;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationOutputWindow;

/// <summary>
/// Protected, read-only, bodyless sealed text window of one output stream of one recorded project verification execution (ADR-0069),
/// restricted to <c>stdout</c> and <c>stderr</c>, requiring no Agent or local-sequence membership. An unknown project, an absent or
/// foreign execution and an unsupported stream are one fixed 404 that repeats no value. An offset or byte cap outside its bounds, or
/// any other query, is a 400, never clamped. Every other outcome is a 200 with an explicit status; bytes are served only through the
/// artifact store's integrity-verifying sealed read.
/// </summary>
public sealed class GetProjectVerificationOutputWindowEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpGet("{projectId:guid}/verification-history/{executionId:guid}/artifact-window/{stream}")]
    [TypeFilter(typeof(ProjectVerificationWindowGuardFilter))]
    [ProducesResponseType<GetProjectVerificationOutputWindowResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetProjectVerificationOutputWindowResponse>> GetProjectVerificationOutputWindow(
        [FromRoute] Guid projectId,
        [FromRoute] Guid executionId,
        [FromRoute] string stream,
        [FromQuery, ModelBinder(typeof(ProjectVerificationOffsetModelBinder))] long? fromOffset,
        [FromQuery, ModelBinder(typeof(ProjectVerificationMaxBytesModelBinder))] int? maxBytes,
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
                Result<GetProjectVerificationOutputWindowQueryResult>.Failure(ProjectVerificationInspectionErrors.NotFound()), HttpContext);
        }

        var result = await mediator.SendAsync(
            new GetProjectVerificationOutputWindowQuery(
                projectId,
                executionId,
                selected,
                fromOffset ?? 0,
                maxBytes ?? GetProjectVerificationOutputWindowQuery.DefaultMaxBytes),
            cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetProjectVerificationOutputWindowResponse(
            value.ProjectId,
            value.ExecutionId,
            value.Stream == VerificationOutputPurpose.StandardOutput ? "stdout" : "stderr",
            value.Status.ToString(),
            value.Artifact is { } artifact ? GetProjectVerificationInspectionEndpoint.MapStream(artifact) : null,
            value.Text,
            value.NextOffset,
            value.TotalLength));
    }
}
