using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Errors;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace DevalCopilot.Api.Features.Runs.GetGitHubBranchPublicationApproval;

/// <summary>
/// Refuses, before model binding and dispatch, any read of the recorded publication approval that carries a query key, a declared body
/// length, any transfer encoding (including an empty chunked one) or a readable body byte (ADR-0048). One byte is peeked so an undeclared
/// body is seen too. The refusals are the fixed non-echoing 400 <see cref="GitHubBranchPublicationApprovalErrors.InvalidRequest"/> and
/// <see cref="GitHubBranchPublicationApprovalErrors.BodyNotAllowed"/>; nothing the caller sent is repeated.
/// </summary>
public sealed class PublicationApprovalReadGuardFilter(IResultProblemDetailsFactory problemDetails) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.Query.Count > 0 || request.QueryString.Value is { Length: > 1 })
        {
            context.Result = problemDetails.CreateResponse(
                Result.Failure(GitHubBranchPublicationApprovalErrors.InvalidRequest()), context.HttpContext);
            return;
        }

        if (request.ContentLength is > 0 || request.Headers.ContainsKey(HeaderNames.TransferEncoding)
            || await request.Body.ReadAsync(new byte[1], context.HttpContext.RequestAborted) > 0)
        {
            context.Result = problemDetails.CreateResponse(
                Result.Failure(GitHubBranchPublicationApprovalErrors.BodyNotAllowed()), context.HttpContext);
            return;
        }

        await next();
    }
}
