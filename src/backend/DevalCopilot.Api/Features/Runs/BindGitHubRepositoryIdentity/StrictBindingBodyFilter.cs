using System.Text.Json;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DevalCopilot.Api.Features.Runs.BindGitHubRepositoryIdentity;

/// <summary>
/// Refuses, before model binding and dispatch, any identity binding body that is not exactly one JSON object with the four expected
/// members, each exactly once, spelled exactly and holding the expected kind of value (ADR-0043). The permissive default binder ignores
/// unknown members, keeps the last duplicate and accepts any casing; this operation accepts none of that. The refusal is the fixed
/// non-echoing <see cref="GitHubRepositoryIdentityBindingErrors.Invalid"/> and the body is never repeated. The 8 KiB limit is enforced by
/// the request size limit on the action; a non-JSON content type is left to the framework's own 415.
/// </summary>
public sealed class StrictBindingBodyFilter(IResultProblemDetailsFactory problemDetails) : IAsyncResourceFilter
{
    private static readonly string[] Members = ["operationId", "commitSha", "targetId", "repositoryNodeId"];

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!request.HasJsonContentType())
        {
            await next();
            return;
        }

        request.EnableBuffering();
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, context.HttpContext.RequestAborted);
        request.Body.Position = 0;

        if (!IsExactShape(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)))
        {
            context.Result = problemDetails.CreateResponse(
                Result.Failure(GitHubRepositoryIdentityBindingErrors.Invalid()), context.HttpContext);
            return;
        }

        await next();
    }

    private static bool IsExactShape(ReadOnlySpan<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(
                body.ToArray(), new JsonDocumentOptions { MaxDepth = 2, AllowDuplicateProperties = false });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var properties = document.RootElement.EnumerateObject().ToArray();
            if (properties.Length != Members.Length)
            {
                return false;
            }

            foreach (var member in Members)
            {
                var matches = properties.Where(property => string.Equals(property.Name, member, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }
            }

            return Guid.TryParseExact(Text(properties, "operationId"), "D", out _)
                && Guid.TryParseExact(Text(properties, "targetId"), "D", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Text(JsonProperty[] properties, string name) =>
        properties.Single(property => string.Equals(property.Name, name, StringComparison.Ordinal)).Value.GetString();
}
