using System.Net;
using System.Text.Json;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>One read of the receipt endpoint: the status and the exact body, parsed once so a test can compare bytes and fields.</summary>
internal sealed record ReceiptReading(HttpStatusCode Status, string Body)
{
    public JsonElement Root => JsonDocument.Parse(Body).RootElement.Clone();

    public string State => Root.GetProperty("state").GetString()!;

    public JsonElement Receipt => Root.GetProperty("receipt");
}
