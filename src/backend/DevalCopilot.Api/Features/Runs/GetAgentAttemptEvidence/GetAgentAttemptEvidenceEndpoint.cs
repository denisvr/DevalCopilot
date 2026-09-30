using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptEvidence;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptEvidence;

/// <summary>
/// Protected, read-only evidence metadata for one Agent attempt selected from the run history,
/// resolved by the exact run and attempt identity. An unknown, foreign-run, or non-Agent attempt is a
/// safe 404; an attempt whose persisted identity is incoherent is a real 200 with
/// <c>identityValid: false</c> and nothing else disclosed. Independent of any collaboration message;
/// the message-linked evidence route is unchanged. Performs no provider call or artifact read.
/// </summary>
public sealed class GetAgentAttemptEvidenceEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/agent-attempts/{attemptId:guid}/evidence")]
    public async Task<ActionResult<AgentAttemptEvidenceResponse>> GetAgentAttemptEvidence(
        [FromRoute] Guid runId, [FromRoute] Guid attemptId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetAgentAttemptEvidenceQuery(runId, attemptId), cancellationToken);

        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new AgentAttemptEvidenceResponse(
            value.IdentityValid,
            value.AttemptId,
            value.AttemptNumber,
            value.AttemptStatus?.ToString(),
            value.ClaimedAtUtc,
            value.CompletedAtUtc,
            value.Provider?.ToString(),
            value.Role?.ToString(),
            value.ResponseContract?.ToString(),
            value.Outcome?.ToString(),
            value.DispatchedAtUtc,
            AgentProcessExecutionResponse.FromDomain(value.IdentityValid, value.ProcessExecution, value.Timeout),
            AgentTokenUsageResponse.FromDomain(value.IdentityValid, value.TokenUsage),
            value.Artifacts
                .Select(artifact => new AgentAttemptArtifactMetadataResponse(
                    artifact.Purpose.ToString(), artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome.ToString()))
                .ToArray(),
            ClaudeMutationTurnLimitResponse.FromDomain(value.MaxTurns),
            value.RepairSourceAttemptId,
            value.RepairSourceAttemptNumber));
    }
}
