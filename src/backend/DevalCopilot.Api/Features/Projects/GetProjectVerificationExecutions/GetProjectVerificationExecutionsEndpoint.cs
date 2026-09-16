using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationExecutions;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationExecutions;

public sealed class GetProjectVerificationExecutionsEndpoint(IApplicationMediator mediator) : ProjectsBaseEndpoint
{
    [HttpGet("{projectId:guid}/verification-executions")]
    public async Task<ActionResult<IReadOnlyList<VerificationExecutionResponse>>> GetProjectVerificationExecutions(
        Guid projectId, CancellationToken cancellationToken)
    {
        var results = await mediator.SendAsync(new GetProjectVerificationExecutionsQuery(projectId), cancellationToken);
        return Ok(results.Select(result => new VerificationExecutionResponse(
            result.VerificationExecutionId,
            result.VerificationCommandId,
            result.GitCheckpointId,
            result.CheckpointFingerprintSha256,
            result.ExecutionNumber,
            result.Status.ToString(),
            result.IsDispatched,
            result.Outcome?.ToString(),
            result.ExitCode,
            result.HasStandardOutput,
            result.HasStandardError,
            result.StandardOutputTruncated,
            result.StandardErrorTruncated,
            result.StandardOutputCaptureOutcome?.ToString(),
            result.StandardErrorCaptureOutcome?.ToString(),
            result.ClaimedAtUtc,
            result.CompletedAtUtc)).ToArray());
    }
}
