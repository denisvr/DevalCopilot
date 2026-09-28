using System.Diagnostics;
using System.Text.Json;

namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// The bounded read/write handle a caller-supplied exchange uses after the App Server handshake
/// completes. This is a closed surface, never a general-purpose JSON-RPC escape hatch: it exposes
/// exactly the two reviewed read-only methods this application ever sends after the handshake —
/// <c>account/rateLimits/read</c> and <c>model/list</c> — and builds each request's fixed JSON
/// shape itself from validated primitive parameters (a request id, and, for <c>model/list</c>, a
/// bounded page size and an already-validated cursor). No caller can write an arbitrary raw JSON
/// payload through this type.
/// </summary>
internal sealed class CodexAppServerChannel(Process process, BoundedJsonLineScanner scanner)
{
    private const string AccountRateLimitsReadMethod = "account/rateLimits/read";
    private const string ModelListMethod = "model/list";

    /// <summary>Sends the fixed, parameterless <c>account/rateLimits/read</c> request and returns
    /// its correlated reply.</summary>
    internal async Task<JsonElement?> SendAccountRateLimitsReadAsync(int requestId, CancellationToken cancellationToken)
    {
        var request = new Dictionary<string, object?> { ["method"] = AccountRateLimitsReadMethod, ["id"] = requestId };
        return await SendAndReadAsync(request, requestId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends one bounded, page-cursor <c>model/list</c> request — always
    /// <c>includeHidden: false</c> — and returns its correlated reply. <paramref name="cursor"/>
    /// must already be the caller's own previously validated <c>nextCursor</c> value; this method
    /// never accepts or forwards an arbitrary string.</summary>
    internal async Task<JsonElement?> SendModelListAsync(int requestId, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?> { ["includeHidden"] = false, ["limit"] = pageSize };
        if (cursor is not null)
        {
            parameters["cursor"] = cursor;
        }

        var request = new Dictionary<string, object?> { ["method"] = ModelListMethod, ["id"] = requestId, ["params"] = parameters };
        return await SendAndReadAsync(request, requestId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement?> SendAndReadAsync(
        Dictionary<string, object?> request, int requestId, CancellationToken cancellationToken)
    {
        await CodexAppServerSession.WriteLineAsync(process, JsonSerializer.Serialize(request), cancellationToken).ConfigureAwait(false);
        return await CodexAppServerSession.ReadCorrelatedResponseAsync(scanner, requestId, cancellationToken).ConfigureAwait(false);
    }
}
