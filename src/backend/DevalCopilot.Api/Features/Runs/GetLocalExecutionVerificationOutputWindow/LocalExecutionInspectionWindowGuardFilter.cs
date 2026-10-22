using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Results;
using DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceInspection;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionVerificationOutputWindow;

/// <summary>
/// Refuses, before model binding and dispatch, a verification output window request (ADR-0061) that carries a request body, or a query
/// other than the exact ordinal keys <c>fromOffset</c> and <c>maxBytes</c>, each with exactly one value. Keys are judged after normal URL
/// decoding, so an unknown key, a differently cased key, an empty key, a repeated key and loose query text are all the fixed non-echoing
/// 400 <see cref="LocalExecutionInspectionErrors.QueryNotAllowed"/>; a body is <see cref="LocalExecutionInspectionErrors.BodyNotAllowed"/>.
/// The values of the two admitted keys keep the existing shared validation contract and are never judged here.
/// </summary>
public sealed class LocalExecutionInspectionWindowGuardFilter(IResultProblemDetailsFactory problemDetails) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.Query.Any(pair => pair.Key is not ("fromOffset" or "maxBytes") || pair.Value.Count != 1)
            || (request.Query.Count == 0 && request.QueryString.Value is { Length: > 1 }))
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
