using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceInspection;

/// <summary>
/// Refuses, before model binding and dispatch, a recorded-sequence index or selected-step request (ADR-0061) that carries any query key or
/// any query text, a declared body length, a transfer encoding or a readable body byte. The refusals are the fixed non-echoing 400
/// <see cref="LocalExecutionInspectionErrors.QueryNotAllowed"/> and <see cref="LocalExecutionInspectionErrors.BodyNotAllowed"/>.
/// </summary>
public sealed class LocalExecutionInspectionBodylessGuardFilter(IResultProblemDetailsFactory problemDetails) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.Query.Count > 0 || request.QueryString.Value is { Length: > 1 })
        {
            context.Result = problemDetails.CreateResponse(Result.Failure(LocalExecutionInspectionErrors.QueryNotAllowed()), context.HttpContext);
            return;
        }

        if (await LocalExecutionInspectionBodyGuard.HasBodyAsync(request, context.HttpContext.RequestAborted))
        {
            context.Result = problemDetails.CreateResponse(Result.Failure(LocalExecutionInspectionErrors.BodyNotAllowed()), context.HttpContext);
            return;
        }

        await next();
    }
}
