using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.EnvironmentReadiness.GetCodexModelCatalog;

public sealed class GetCodexModelCatalogEndpoint(IApplicationMediator mediator) : EnvironmentBaseEndpoint
{
    [HttpGet("codex-model-catalog")]
    public async Task<ActionResult<CodexModelCatalogResponse>> GetCodexModelCatalog(CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetCodexModelCatalogQuery(), cancellationToken);

        return Ok(new CodexModelCatalogResponse(
            result.Status.ToString(),
            result.RetrievedAtUtc,
            result.Models.Select(model => new CodexModelCatalogEntryResponse(
                model.Id, model.DisplayName, model.SupportedReasoningEfforts, model.DefaultReasoningEffort)).ToArray()));
    }
}
