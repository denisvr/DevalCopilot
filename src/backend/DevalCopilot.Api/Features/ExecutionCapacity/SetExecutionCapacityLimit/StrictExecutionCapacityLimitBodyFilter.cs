using System.Text.Json;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DevalCopilot.Api.Features.ExecutionCapacity.SetExecutionCapacityLimit;

/// <summary>
/// Refuses, before model binding and dispatch, any limit change (ADR-0059) that carries a query key, whose body exceeds 512 bytes
/// (declared, chunked or undeclared: at most one byte more than the limit is ever read) or that is not exactly one JSON object whose single
/// member is spelled exactly <c>limit</c>, appears once, is not written with a JSON escape and holds a plain JSON integer, one or two. The
/// permissive default binder would accept casing, unknown members, the last duplicate and numeric coercions (<c>1.0</c>, <c>"1"</c>);
/// this operation accepts none of them. The refusals are the fixed non-echoing 400 <see cref="ExecutionCapacityErrors.InvalidRequest"/>
/// and 413 <see cref="ExecutionCapacityErrors.BodyTooLarge"/>; the body is never repeated. A non-JSON content type is left to the
/// framework's own 415.
/// </summary>
public sealed class StrictExecutionCapacityLimitBodyFilter(IResultProblemDetailsFactory problemDetails) : IAsyncResourceFilter
{
    internal const int MaximumBodyBytes = 512;

    private const string Member = "limit";

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.Query.Count > 0 || request.QueryString.Value is { Length: > 1 })
        {
            context.Result = problemDetails.CreateResponse(Result.Failure(ExecutionCapacityErrors.InvalidRequest()), context.HttpContext);
            return;
        }

        if (!request.HasJsonContentType())
        {
            await next();
            return;
        }

        if (request.ContentLength > MaximumBodyBytes)
        {
            context.Result = TooLarge(context.HttpContext);
            return;
        }

        request.EnableBuffering();
        var body = new byte[MaximumBodyBytes + 1];
        var length = 0;
        try
        {
            int read;
            while (length < body.Length
                   && (read = await request.Body.ReadAsync(body.AsMemory(length), context.HttpContext.RequestAborted)) > 0)
            {
                length += read;
            }
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            context.Result = TooLarge(context.HttpContext);
            return;
        }

        if (length > MaximumBodyBytes)
        {
            context.Result = TooLarge(context.HttpContext);
            return;
        }

        request.Body.Position = 0;
        if (!IsExactShape(body.AsSpan(0, length)))
        {
            context.Result = problemDetails.CreateResponse(Result.Failure(ExecutionCapacityErrors.InvalidRequest()), context.HttpContext);
            return;
        }

        await next();
    }

    private static ObjectResult TooLarge(HttpContext httpContext)
    {
        var error = ExecutionCapacityErrors.BodyTooLarge();
        return new ObjectResult(new ApiProblemDetails
        {
            Type = "urn:devalente:problem:payload-too-large",
            Title = "The request body is too large.",
            Status = StatusCodes.Status413PayloadTooLarge,
            Detail = "See the errors property for details.",
            Instance = httpContext.Request.Path,
            TraceId = System.Diagnostics.Activity.Current?.Id ?? httpContext.TraceIdentifier,
            Errors = [new ApiError(error.Code, error.Description, null, null)],
        })
        {
            StatusCode = StatusCodes.Status413PayloadTooLarge,
        };
    }

    private static bool IsExactShape(ReadOnlySpan<byte> body)
    {
        if (body.Length is 0 or > MaximumBodyBytes)
        {
            return false;
        }

        try
        {
            var reader = new Utf8JsonReader(body, new JsonReaderOptions { MaxDepth = 2 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject
                || !reader.Read() || reader.TokenType != JsonTokenType.PropertyName || reader.ValueIsEscaped
                || !reader.ValueTextEquals(Member)
                || !reader.Read() || reader.TokenType != JsonTokenType.Number)
            {
                return false;
            }

            // A plain one-character integer token: no sign, fraction, exponent or leading zero can reach here as one or two.
            var plain = reader.ValueSpan.Length == 1 && reader.TryGetInt32(out var limit) && ExecutionCapacityConfigurationRevision.IsSupportedLimit(limit);
            return plain && reader.Read() && reader.TokenType == JsonTokenType.EndObject && !reader.Read();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}
