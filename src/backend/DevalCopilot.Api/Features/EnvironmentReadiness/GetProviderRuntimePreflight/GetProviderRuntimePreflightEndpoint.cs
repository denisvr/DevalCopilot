using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetProviderRuntimePreflight;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.EnvironmentReadiness.GetProviderRuntimePreflight;

public sealed class GetProviderRuntimePreflightEndpoint(IApplicationMediator mediator) : EnvironmentBaseEndpoint
{
    [HttpGet("provider-runtimes")]
    public async Task<ActionResult<IReadOnlyList<ProviderRuntimePreflightResponse>>> GetProviderRuntimePreflight(
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetProviderRuntimePreflightQuery(), cancellationToken);

        return Ok(result.Select(runtime => new ProviderRuntimePreflightResponse(
            runtime.Provider.ToString(),
            runtime.Status.ToString(),
            runtime.ObservedVersion,
            runtime.EvidenceObservedAtUtc,
            runtime.EvidenceFreshness.ToString(),
            runtime.ReasonCode,
            runtime.ReasonMessage,
            runtime.Authentication.ToString(),
            runtime.ModelCatalog.ToString(),
            runtime.ReasoningEffort.ToString(),
            runtime.PermissionMode.ToString(),
            runtime.ContextUsage.ToString(),
            runtime.Compaction.ToString(),
            runtime.Sessions.ToString(),
            runtime.AccountUsage.ToString())).ToArray());
    }
}
