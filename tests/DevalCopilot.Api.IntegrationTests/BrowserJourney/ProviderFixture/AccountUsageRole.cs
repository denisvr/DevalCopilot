using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>
/// The Codex double's closed App Server mode: exactly the handshake the strict account-usage observation sends (the <c>initialize</c>
/// request, the <c>initialized</c> notification, then the one <c>account/rateLimits/read</c> request) and nothing else, answered from a
/// script the journey itself writes into the owned root. Any other line ends the process with a fixed refusal, so an adapter that sent
/// another method, another shape or an extra flag fails the journey instead of being served. Each launch is one read: a counter file
/// in the owned root picks the scripted answer (the last one repeats), so the claim-time and dispatch-time observations of one scenario
/// can differ deterministically. It never inherits configuration and never reads or writes outside the owned root.
/// </summary>
public static class AccountUsageRole
{
    private const string InitializeRequest =
        """{"method":"initialize","id":0,"params":{"clientInfo":{"name":"DevalCopilot","title":"DevalCopilot","version":"1.0.0"},"capabilities":{}}}""";

    private const string InitializedNotification = """{"method":"initialized","params":{}}""";

    private const string ReadRequest = """{"method":"account/rateLimits/read","id":1}""";

    public static int Run(OwnedLocation location, TextReader input, TextWriter output)
    {
        Require(input.ReadLine(), InitializeRequest, "initialize");
        output.Write("{\"id\":0,\"result\":{}}\n");
        output.Flush();
        Require(input.ReadLine(), InitializedNotification, "initialized");
        Require(input.ReadLine(), ReadRequest, "account/rateLimits/read");

        var reads = ReadScript(location);
        var index = NextReadIndex(location);
        var entry = reads[Math.Min(index, reads.Count - 1)]!.AsObject();
        InvocationLog.Append(location, new Dictionary<string, object?>
        {
            ["role"] = "codex",
            ["kind"] = "account_usage",
            ["usageReadIndex"] = index,
        });

        output.Write(Reply(entry) + "\n");
        output.Flush();
        return 0;
    }

    private static void Require(string? line, string expected, string what)
    {
        if (!string.Equals(line, expected, StringComparison.Ordinal))
        {
            throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The App Server exchange does not match the closed contract at " + what + ".");
        }
    }

    private static JsonArray ReadScript(OwnedLocation location)
    {
        var path = location.RequireUsageScript();
        try
        {
            var reads = JsonNode.Parse(File.ReadAllText(path))?["reads"] as JsonArray;
            return reads is { Count: > 0 } ? reads : throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The usage script carries no reads.");
        }
        catch (JsonException)
        {
            throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The usage script is not valid JSON.");
        }
    }

    private static int NextReadIndex(OwnedLocation location)
    {
        var counter = location.UsageCounterPath;
        var index = File.Exists(counter) && int.TryParse(File.ReadAllText(counter), NumberStyles.None, CultureInfo.InvariantCulture, out var seen) ? seen : 0;
        Directory.CreateDirectory(Path.GetDirectoryName(counter)!);
        File.WriteAllText(counter, (index + 1).ToString(CultureInfo.InvariantCulture));
        return index;
    }

    /// <summary>One scripted read: <c>primary</c>/<c>secondary</c> used percentages (either may be absent), an optional
    /// <c>reachedType</c> string, and <c>unavailable</c> to answer with a protocol error instead of a snapshot.</summary>
    private static string Reply(JsonObject entry)
    {
        if (entry["unavailable"]?.GetValue<bool>() == true)
        {
            return """{"id":1,"error":{"code":-32000,"message":"not available"}}""";
        }

        var resetsAt = DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeSeconds();
        JsonNode? Window(string name) => entry[name] is { } value
            ? new JsonObject
            {
                ["usedPercent"] = value.GetValue<int>(),
                ["windowDurationMins"] = 300,
                ["resetsAt"] = resetsAt,
            }
            : null;

        JsonObject Snapshot() => new()
        {
            ["limitId"] = "codex",
            ["primary"] = Window("primary"),
            ["secondary"] = Window("secondary"),
            ["rateLimitReachedType"] = entry["reachedType"]?.GetValue<string>(),
        };

        return new JsonObject
        {
            ["id"] = 1,
            ["result"] = new JsonObject
            {
                ["rateLimits"] = Snapshot(),
                ["rateLimitsByLimitId"] = new JsonObject { ["codex"] = Snapshot() },
            },
        }.ToJsonString();
    }
}
