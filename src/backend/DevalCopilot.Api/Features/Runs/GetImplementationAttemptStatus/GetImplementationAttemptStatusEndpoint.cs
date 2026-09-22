using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;
using DevalCopilot.Application.Features.Runs.Queries.GetImplementationAttemptStatus;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetImplementationAttemptStatus;

/// <summary>
/// Bounded status, checkpoint, changed-file, assignment, and artifact metadata for the most recent initial
/// implementation attempt on a run. An unknown run is a safe 404; an existing run with no such
/// attempt yet is a real 200 body with <c>hasAttempt: false</c> — never an ambiguous null/204.
/// Never exposes a local path, prompt, manifest, credential, environment value, command line, or
/// raw transcript.
/// </summary>
public sealed class GetImplementationAttemptStatusEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/agent-attempts/implementation")]
    public async Task<ActionResult<ImplementationAttemptStatusResponse>> GetImplementationAttemptStatus(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetImplementationAttemptStatusQuery(runId), cancellationToken);

        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new ImplementationAttemptStatusResponse(
            value.HasAttempt,
            value.AttemptId,
            value.AttemptNumber,
            value.PlanProposalMessageId,
            value.Status?.ToString(),
            value.Outcome?.ToString(),
            value.StartingGitCheckpointId,
            value.StartingCheckpointFingerprintSha256,
            value.ResultGitCheckpointId,
            value.ResultCheckpointFingerprintSha256,
            value.ExecutionReportSummary,
            value.ChangedRelativePaths,
            value.ClaimedAtUtc,
            value.DispatchedAtUtc,
            value.CompletedAtUtc,
            value.Artifacts
                .Select(artifact => new AgentAttemptArtifactMetadataResponse(
                    artifact.Purpose.ToString(), artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome.ToString()))
                .ToArray(),
            value.Assignment?.Provider.ToString(),
            value.Role?.ToString(),
            value.Assignment?.RequestedModel,
            value.Assignment?.ObservedModel,
            value.Assignment?.RequestedEffort,
            value.Assignment?.ObservedEffort,
            value.Assignment?.PermissionProfile.ToString(),
            value.Assignment?.AdapterContractVersion));
    }
}
