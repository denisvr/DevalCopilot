using System.Globalization;
using System.Text.Json;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DevalCopilot.Api.Features.Runs.ReleaseLocalExecutionOwnership;

/// <summary>
/// Refuses, before model binding and dispatch, any ownership-release request (ADR-0065) that carries a query key, whose body exceeds 512
/// bytes (declared, chunked or undeclared: at most one byte more than the limit is ever read) or that is not exactly one JSON object with
/// the four expected string members, each exactly once and spelled exactly: observationId in the exact 36-character form, a canonical
/// positive confirmationVersion, a confirmationSha256 of 64 lowercase hexadecimal characters and a confirmationExpiresAtUtc in the
/// canonical UTC round-trip form. The permissive default binder ignores unknown members, keeps the last duplicate and accepts any
/// casing; this operation accepts none of that, and a member name or value written with a JSON escape is refused even when it decodes to
/// an expected one. The refusals are the fixed non-echoing 400 <see cref="LocalExecutionOwnershipReleaseErrors.InvalidRequest"/> and 413
/// <see cref="LocalExecutionOwnershipReleaseErrors.BodyTooLarge"/>; the body is never repeated. A non-JSON content type is left to the
/// framework's own 415.
/// </summary>
public sealed class StrictLocalExecutionOwnershipReleaseBodyFilter(IResultProblemDetailsFactory problemDetails) : IAsyncResourceFilter
{
    internal const int MaximumBodyBytes = 512;

    private static readonly string[] Members = ["observationId", "confirmationVersion", "confirmationSha256", "confirmationExpiresAtUtc"];

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.Query.Count > 0 || request.QueryString.Value is { Length: > 1 })
        {
            context.Result = Invalid(context.HttpContext);
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
            context.Result = Invalid(context.HttpContext);
            return;
        }

        await next();
    }

    private ObjectResult Invalid(HttpContext httpContext) =>
        problemDetails.CreateResponse(Result.Failure(LocalExecutionOwnershipReleaseErrors.InvalidRequest()), httpContext);

    private static ObjectResult TooLarge(HttpContext httpContext)
    {
        var error = LocalExecutionOwnershipReleaseErrors.BodyTooLarge();
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
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.ValueIsEscaped)
                {
                    return false;
                }

                var name = reader.GetString()!;
                if (Array.IndexOf(Members, name) < 0 || seen.ContainsKey(name) || !reader.Read() || reader.TokenType != JsonTokenType.String)
                {
                    return false;
                }

                if (reader.ValueIsEscaped)
                {
                    return false;
                }

                seen[name] = reader.GetString()!;
            }

            if (reader.TokenType != JsonTokenType.EndObject || reader.Read() || seen.Count != Members.Length)
            {
                return false;
            }

            return IsExactGuid(seen["observationId"])
                && IsCanonicalPositiveInteger(seen["confirmationVersion"])
                && LocalExecutionSequencePolicy.IsLowercaseSha256(seen["confirmationSha256"])
                && LocalExecutionOwnershipReleaseConfirmation.TryParseTime(seen["confirmationExpiresAtUtc"], out _);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Guid parsing tolerates surrounding white space; the exact 36-character form is required so nothing is trimmed.</summary>
    private static bool IsExactGuid(string value) =>
        value.Length == 36 && Guid.TryParseExact(value, "D", out var parsed) && parsed != Guid.Empty;

    private static bool IsCanonicalPositiveInteger(string value) =>
        value.Length is > 0 and <= 9
        && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
        && parsed > 0
        && string.Equals(parsed.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal);
}
