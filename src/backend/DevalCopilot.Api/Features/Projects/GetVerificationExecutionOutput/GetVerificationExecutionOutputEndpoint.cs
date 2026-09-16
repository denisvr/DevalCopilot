using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Queries.GetVerificationExecutionOutput;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetVerificationExecutionOutput;

public sealed class GetVerificationExecutionOutputEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problems) : ProjectsBaseEndpoint
{
    private const int DefaultMaxBytes = 16 * 1024;
    private const int HardMaxBytes = 64 * 1024;
    private const int MinMaxBytes = 64;

    [HttpGet("{projectId:guid}/verification-executions/{verificationExecutionId:guid}/output/{purpose}")]
    public async Task<ActionResult<VerificationExecutionOutputQueryResult>> GetVerificationExecutionOutput(
        Guid projectId,
        Guid verificationExecutionId,
        string purpose,
        [FromQuery] long fromOffset,
        [FromQuery] int? maxBytes,
        CancellationToken cancellationToken)
    {
        if (!TryMapPurpose(purpose, out var mappedPurpose))
        {
            return NotFound();
        }

        if (fromOffset < 0)
        {
            return BadRequest();
        }

        var boundedMaxBytes = Math.Clamp(maxBytes ?? DefaultMaxBytes, MinMaxBytes, HardMaxBytes);
        var result = await mediator.SendAsync(
            new GetVerificationExecutionOutputQuery(projectId, verificationExecutionId, mappedPurpose, fromOffset, boundedMaxBytes), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : problems.CreateResponse(result, HttpContext);
    }

    private static bool TryMapPurpose(string value, out VerificationOutputPurpose purpose)
    {
        switch (value.ToLowerInvariant())
        {
            case "stdout":
                purpose = VerificationOutputPurpose.StandardOutput;
                return true;
            case "stderr":
                purpose = VerificationOutputPurpose.StandardError;
                return true;
            default:
                purpose = default;
                return false;
        }
    }
}
