using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Projects.GetProjectVerificationHistory;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationInspection;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationInspection;

/// <summary>
/// Protected, read-only, bodyless metadata of one recorded project verification execution and its two sealed-stream descriptors
/// (ADR-0069). It accepts no query key and no body, opens no artifact file and requires no Agent or local-sequence membership. An unknown
/// project and an absent or foreign execution are one fixed 404 that repeats no value; a member whose recorded facts are incoherent is a
/// 200 <c>Unavailable</c>, never absent.
/// </summary>
public sealed class GetProjectVerificationInspectionEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : ProjectsBaseEndpoint
{
    [HttpGet("{projectId:guid}/verification-history/{executionId:guid}")]
    [TypeFilter(typeof(ProjectVerificationDetailGuardFilter))]
    [ProducesResponseType<GetProjectVerificationInspectionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetProjectVerificationInspectionResponse>> GetProjectVerificationInspection(
        [FromRoute] Guid projectId, [FromRoute] Guid executionId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetProjectVerificationInspectionQuery(projectId, executionId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetProjectVerificationInspectionResponse(
            value.ProjectId, value.ExecutionId, value.Status.ToString(), value.Execution is { } execution ? Map(execution) : null));
    }

    internal static ProjectVerificationInspectionStreamResponse MapStream(RecordedVerificationOutputDescriptor descriptor) => new(
        descriptor.Status.ToString(), descriptor.ArtifactId, descriptor.ByteLength, descriptor.CaptureOutcome?.ToString(), descriptor.Truncated);

    private static string Instant(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static ProjectVerificationInspectionExecutionResponse Map(ProjectVerificationInspectionExecution execution) => new(
        execution.ExecutionNumber,
        execution.CommandId,
        execution.CommandName,
        execution.WorkspaceId,
        execution.CheckpointId,
        execution.CheckpointNumber,
        execution.ClaimFingerprintSha256,
        execution.CompletionFingerprintSha256,
        execution.Status.ToString(),
        execution.Outcome?.ToString(),
        execution.ExitCode,
        Instant(execution.ClaimedAtUtc),
        execution.DispatchedAtUtc is { } dispatched ? Instant(dispatched) : null,
        execution.CompletedAtUtc is { } completed ? Instant(completed) : null,
        MapStream(execution.StandardOutput),
        MapStream(execution.StandardError));
}
