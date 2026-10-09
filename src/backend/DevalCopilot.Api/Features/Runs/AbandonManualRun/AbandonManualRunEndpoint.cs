using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.AbandonManualRun;

/// <summary>
/// Explicitly abandons an inactive manual Agent run (ADR-0031). The host decides from fresh authority that nothing of the project is
/// active or ambiguous, records the Abandoned lifecycle with the human reason and one Human-authored event atomically, and cancels,
/// repairs and deletes nothing. A replay of the same normalized reason returns the recorded result; a different reason conflicts.
/// The request body is bounded to 8 KiB.
/// </summary>
public sealed class AbandonManualRunEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = 8 * 1024;

    [HttpPost("{runId:guid}/abandon")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    public async Task<ActionResult<AbandonManualRunResponse>> AbandonManualRun(
        [FromRoute] Guid runId, [FromBody] AbandonManualRunRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new AbandonManualRunCommand(runId, request.Reason), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new AbandonManualRunResponse(
                result.Value.RunId, result.Value.ExecutionNumber, result.Value.Reason, result.Value.AbandonedAtUtc));
    }
}
