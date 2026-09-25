using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationMessageEvidence;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetCollaborationMessageEvidence;

/// <summary>
/// Bounded, read-only drill-down from one collaboration card to the exact Attempt that produced
/// it, resolved solely through the message's own durable <c>AttemptId</c> foreign key. An unknown
/// run/message pair is a safe 404; an existing message with no linked attempt, or one whose linked
/// attempt row cannot be found, is a real 200 body with an explicit, distinct
/// <c>evidenceStatus</c> — never an ambiguous null/204, and never a substituted attempt. Performs
/// no Git capture and never implies a historical checkpoint reflects the run's current source
/// state.
/// </summary>
public sealed class GetCollaborationMessageEvidenceEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/collaboration-messages/{messageId:guid}/evidence")]
    public async Task<ActionResult<CollaborationMessageEvidenceResponse>> GetCollaborationMessageEvidence(
        [FromRoute] Guid runId, [FromRoute] Guid messageId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetCollaborationMessageEvidenceQuery(runId, messageId), cancellationToken);

        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new CollaborationMessageEvidenceResponse(
            value.Status.ToString(),
            value.AttemptId,
            value.AttemptNumber,
            value.AttemptKind?.ToString(),
            value.AttemptStatus?.ToString(),
            value.ClaimedAtUtc,
            value.CompletedAtUtc,
            value.AgentProvider?.ToString(),
            value.AgentRole?.ToString(),
            value.AgentResponseContract?.ToString(),
            value.AgentOutcome?.ToString(),
            value.AgentDispatchedAtUtc,
            value.StartingGitCheckpointId,
            value.StartingCheckpointFingerprintSha256,
            value.ResultGitCheckpointId,
            value.ResultCheckpointFingerprintSha256,
            AgentProcessExecutionResponse.FromDomain(value.Status == CollaborationMessageEvidenceStatus.HasEvidence, value.ProcessExecution, value.AgentTimeout),
            AgentTokenUsageResponse.FromDomain(value.Status == CollaborationMessageEvidenceStatus.HasEvidence, value.TokenUsage),
            value.Artifacts
                .Select(artifact => new AgentAttemptArtifactMetadataResponse(
                    artifact.Purpose.ToString(), artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome.ToString()))
                .ToArray(),
            value.ArtifactsOmitted,
            value.ArtifactTotalCount));
    }
}
