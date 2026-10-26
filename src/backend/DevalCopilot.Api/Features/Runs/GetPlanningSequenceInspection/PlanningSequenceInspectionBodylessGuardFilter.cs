using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace DevalCopilot.Api.Features.Runs.GetPlanningSequenceInspection;

/// <summary>
/// Refuses, before model binding and dispatch, a recorded planning index or selected-step request (ADR-0068) that carries any query key
/// or any query text, a declared body length, any transfer encoding (including an empty chunked one) or a readable body byte. One byte is
/// peeked so an undeclared body is seen too. The refusals are the fixed non-echoing 400
/// <see cref="PlanningSequenceInspectionErrors.QueryNotAllowed"/> and <see cref="PlanningSequenceInspectionErrors.BodyNotAllowed"/>;
/// nothing the caller sent is read further or repeated.
/// </summary>
public sealed class PlanningSequenceInspectionBodylessGuardFilter(IResultProblemDetailsFactory problemDetails) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.Query.Count > 0 || request.QueryString.Value is { Length: > 1 })
        {
            context.Result = problemDetails.CreateResponse(Result.Failure(PlanningSequenceInspectionErrors.QueryNotAllowed()), context.HttpContext);
            return;
        }

        if (request.ContentLength is > 0
            || request.Headers.ContainsKey(HeaderNames.TransferEncoding)
            || await request.Body.ReadAsync(new byte[1], context.HttpContext.RequestAborted) > 0)
        {
            context.Result = problemDetails.CreateResponse(Result.Failure(PlanningSequenceInspectionErrors.BodyNotAllowed()), context.HttpContext);
            return;
        }

        await next();
    }
}
