using System.Text.Json.Nodes;

namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>
/// Append-only evidence of what the doubles were asked, restricted to a closed allowlist of fields: the role, the invocation
/// kind, the contract name, and message identities. It never holds an argument, a complete manifest, an output text, an
/// environment value, or an authorization header, so it is safe to read from the journey and to attach to a failure.
/// </summary>
public static class InvocationLog
{
    private static readonly HashSet<string> AllowedFields =
    [
        "role", "kind", "contract", "planMessageId", "planMarker", "reportMessageId", "findingCount", "challengeCount",
        "changedPath", "outcome", "guidanceSha256", "guidanceBoundary", "authorizationId", "escalationMessageId",
        "instructionMessageId", "rationaleSha256", "decisionCount", "decisionChallengeIds", "instructionSection", "instructionOrder",
        "instructionBoundary", "instructionBinding", "instructionFiles", "instructionStatuses", "instructionByteLengths", "instructionSha256",
        "instructionTextsVerified", "instructionReferences",
    ];

    public static void Append(OwnedLocation location, IReadOnlyDictionary<string, object?> fields)
    {
        var entry = new JsonObject();
        foreach (var (name, value) in fields)
        {
            if (!AllowedFields.Contains(name))
            {
                throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "A log field outside the allowlist was refused.");
            }

            entry[name] = value switch
            {
                null => null,
                string text => text,
                int number => number,
                Guid guid => guid.ToString(),
                _ => throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "A log value of an unsupported type was refused."),
            };
        }

        var destination = location.RequireLogDestination();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.AppendAllText(destination, entry.ToJsonString() + "\n");
    }
}
