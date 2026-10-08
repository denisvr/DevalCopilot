using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Queries.GetCheckpointApprovalEvidence;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetCheckpointApprovalEvidence;

/// <summary>
/// The complete verification set a human may approve for the exact current checkpoint (ADR-0030), or a fixed refusal. Read-only: it
/// writes nothing and grants no authority; the review request decides again.
/// </summary>
public sealed class GetCheckpointApprovalEvidenceEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpGet("{projectId:guid}/checkpoints/{checkpointId:guid}/approval-evidence")]
    public async Task<ActionResult<CheckpointApprovalEvidenceResponse>> GetCheckpointApprovalEvidence(
        [FromRoute] Guid projectId, [FromRoute] Guid checkpointId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetCheckpointApprovalEvidenceQuery(projectId, checkpointId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var evidence = result.Value;
        return Ok(new CheckpointApprovalEvidenceResponse(
            evidence.ProjectId,
            evidence.WorkspaceId,
            evidence.CheckpointId,
            evidence.CheckpointNumber,
            evidence.FingerprintSha256,
            evidence.Members.Select(member => new CheckpointApprovalEvidenceMemberResponse(
                member.VerificationCommandId,
                member.CommandNumber,
                member.RecipeLabel,
                member.VerificationExecutionId,
                member.ExecutionNumber)).ToArray()));
    }
}
