using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectGitEvidence;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetProjectGitEvidence;

public sealed class GetProjectGitEvidenceEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpGet("{projectId:guid}/workspace/evidence")]
    public async Task<ActionResult<GetProjectGitEvidenceResponse>> GetProjectGitEvidence(
        [FromRoute] Guid projectId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetProjectGitEvidenceQuery(projectId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        return Ok(new GetProjectGitEvidenceResponse(
            result.Value.CheckpointId,
            result.Value.CheckpointNumber,
            result.Value.CapturedAtUtc,
            result.Value.HeadCommitSha,
            result.Value.FingerprintSha256,
            result.Value.ChangedFileCount));
    }
}
