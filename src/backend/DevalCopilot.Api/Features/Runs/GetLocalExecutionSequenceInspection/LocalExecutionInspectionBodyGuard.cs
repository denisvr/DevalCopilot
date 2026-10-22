using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceInspection;

/// <summary>
/// The one shared body admission of the three recorded local execution inspection reads (ADR-0061): a declared body length, any transfer
/// encoding (including an empty chunked one) and a readable body byte without a declared length each make a request bodied. One byte is
/// peeked so an undeclared body is seen too. Nothing the caller sent is read further or repeated.
/// </summary>
internal static class LocalExecutionInspectionBodyGuard
{
    public static async Task<bool> HasBodyAsync(HttpRequest request, CancellationToken cancellationToken) =>
        request.ContentLength is > 0
        || request.Headers.ContainsKey(HeaderNames.TransferEncoding)
        || await request.Body.ReadAsync(new byte[1], cancellationToken) > 0;
}
