using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationCommands;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationCommands;

public sealed class GetProjectVerificationCommandsEndpoint(IApplicationMediator mediator) : ProjectsBaseEndpoint
{
    [HttpGet("{projectId:guid}/verification-commands")]
    public async Task<ActionResult<IReadOnlyList<VerificationCommandResponse>>> GetProjectVerificationCommands(
        [FromRoute] Guid projectId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetProjectVerificationCommandsQuery(projectId), cancellationToken);
        return Ok(result.Select(command => new VerificationCommandResponse(
            command.VerificationCommandId,
            command.CommandNumber,
            command.Name,
            command.ExecutablePath,
            command.Arguments,
            command.TimeoutSeconds,
            command.IsEnabled,
            command.UpdatedAtUtc)));
    }
}
