using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Errors;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionQueueAvailability;

/// <summary>
/// Refuses, before model binding and dispatch, any queued availability request (ADR-0062) that is not exactly a bodyless GET whose raw
/// query string is the single required key <c>planProposalMessageId</c>, once, in exactly this ordinal spelling, with one 36-character
/// <c>D</c>-form identifier and nothing else: no other, repeated, differently cased or percent-escaped key or value, no fragment of a
/// second pair, no body, no declared length and no transfer encoding. The permissive default binder is case-insensitive and keeps the
/// last duplicate, so the raw text is judged here. The refusal is the fixed non-echoing 400
/// <see cref="LocalExecutionSequenceErrors.InvalidRequest"/> or <see cref="LocalExecutionSequenceErrors.BodyNotAllowed"/>; nothing the
/// caller sent is repeated.
/// </summary>
public sealed class StrictLocalExecutionQueueAvailabilityQueryFilter(IResultProblemDetailsFactory problemDetails) : IAsyncResourceFilter
{
    private const string Prefix = "?planProposalMessageId=";

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var raw = request.QueryString.Value;
        if (raw is null
            || raw.Length != Prefix.Length + 36
            || !raw.StartsWith(Prefix, StringComparison.Ordinal)
            || !IsExactGuid(raw[Prefix.Length..]))
        {
            context.Result = problemDetails.CreateResponse(Result.Failure(LocalExecutionSequenceErrors.InvalidRequest()), context.HttpContext);
            return;
        }

        if (request.ContentLength is > 0 || request.Headers.ContainsKey(HeaderNames.TransferEncoding)
            || await request.Body.ReadAsync(new byte[1], context.HttpContext.RequestAborted) > 0)
        {
            context.Result = problemDetails.CreateResponse(Result.Failure(LocalExecutionSequenceErrors.BodyNotAllowed()), context.HttpContext);
            return;
        }

        await next();
    }

    private static bool IsExactGuid(string value) =>
        value.Length == 36 && Guid.TryParseExact(value, "D", out var parsed) && parsed != Guid.Empty;
}
