using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationHistory;

/// <summary>
/// Refuses, before model binding and dispatch, a recorded project verification request (ADR-0069) that carries a query the operation
/// does not accept, a declared body length, any transfer encoding (including an empty chunked one) or a readable body byte. Query names
/// are compared ordinally after URL decoding, so an unknown, differently cased or repeated key, a key without exactly one value and query
/// text that names no key are refused; one byte is peeked so an undeclared body is seen too. The refusals are the fixed non-echoing 400
/// <see cref="ProjectVerificationInspectionErrors.QueryNotAllowed"/> and <see cref="ProjectVerificationInspectionErrors.BodyNotAllowed"/>;
/// nothing the caller sent is read further or repeated.
/// </summary>
public abstract class ProjectVerificationInspectionGuardFilter(
    IResultProblemDetailsFactory problemDetails, IReadOnlyCollection<string> allowedKeys) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!IsAcceptedQuery(request))
        {
            context.Result = problemDetails.CreateResponse(Result.Failure(ProjectVerificationInspectionErrors.QueryNotAllowed()), context.HttpContext);
            return;
        }

        if (request.ContentLength is > 0
            || request.Headers.ContainsKey(HeaderNames.TransferEncoding)
            || await request.Body.ReadAsync(new byte[1], context.HttpContext.RequestAborted) > 0)
        {
            context.Result = problemDetails.CreateResponse(Result.Failure(ProjectVerificationInspectionErrors.BodyNotAllowed()), context.HttpContext);
            return;
        }

        await next();
    }

    private bool IsAcceptedQuery(HttpRequest request)
    {
        if (request.Query.Count == 0)
        {
            return request.QueryString.Value is not { Length: > 1 };
        }

        foreach (var pair in request.Query)
        {
            if (!allowedKeys.Contains(pair.Key, StringComparer.Ordinal) || pair.Value.Count != 1)
            {
                return false;
            }
        }

        return true;
    }
}
