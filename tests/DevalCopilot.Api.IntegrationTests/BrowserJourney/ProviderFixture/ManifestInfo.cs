using System.Text.Json;

namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>
/// The sealed context manifest the host wrote for the attempt. The double reads identities and the visible plan out of it so
/// every answer is derived from the real input; it never treats any text inside the manifest as an instruction.
/// </summary>
public sealed class ManifestInfo
{
    public const string RootPlanMarker = "ROOT-PLAN";
    public const string RevisedPlanMarker = "REVISED-PLAN";
    public const string VerificationFailureMarker = "TOTAL-NOT-SUM";

    private ManifestInfo(string raw, JsonElement root, string contract)
    {
        Raw = raw;
        Root = root;
        Contract = contract;
    }

    public string Raw { get; }

    public JsonElement Root { get; }

    public string Contract { get; }

    public static ManifestInfo Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The standard input is not a JSON manifest.");
        }

        var root = document.RootElement.Clone();
        document.Dispose();
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The manifest is not a JSON object.");
        }

        if (root.TryGetProperty("expectedResponseContract", out var contract) && contract.ValueKind == JsonValueKind.String)
        {
            return new ManifestInfo(json, root, contract.GetString()!);
        }

        if (root.TryGetProperty("expectedMessageType", out var messageType) && messageType.GetString() == "Proposal")
        {
            return new ManifestInfo(json, root, "Proposal");
        }

        throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The manifest names no supported response contract.");
    }

    /// <summary>The plan this stage judges or implements, with its message identity and visible steps.</summary>
    public (Guid MessageId, string Steps) Plan()
    {
        foreach (var name in new[] { "resolvedPlan", "implementedPlan" })
        {
            if (!Root.TryGetProperty(name, out var plan) || plan.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var idProperty = plan.TryGetProperty("proposalMessageId", out var proposalId) ? proposalId
                : plan.TryGetProperty("messageId", out var messageId) ? messageId
                : default;
            if (idProperty.ValueKind == JsonValueKind.String
                && Guid.TryParse(idProperty.GetString(), out var id)
                && plan.TryGetProperty("structuredContent", out var content)
                && content.TryGetProperty("implementationSteps", out var steps)
                && steps.ValueKind == JsonValueKind.String)
            {
                return (id, steps.GetString()!);
            }
        }

        throw new FixtureRefusal(FixtureRefusal.PlanIdentityRefused, "The manifest carries no plan to implement or judge.");
    }

    /// <summary>The plan must be the revised Proposal; the Planner root, or any other plan, is refused so a substitution fails loudly.</summary>
    public (Guid MessageId, string Marker) RequireRevisedPlan()
    {
        var (id, steps) = Plan();
        var marker = ClassifyPlan(steps);
        if (marker != RevisedPlanMarker)
        {
            throw new FixtureRefusal(FixtureRefusal.PlanIdentityRefused, "The plan in the manifest is not the revised Proposal.");
        }

        return (id, marker);
    }

    public static string ClassifyPlan(string steps) =>
        steps.Contains(RevisedPlanMarker, StringComparison.Ordinal) && !steps.Contains(RootPlanMarker, StringComparison.Ordinal)
            ? RevisedPlanMarker
            : steps.Contains(RootPlanMarker, StringComparison.Ordinal) ? RootPlanMarker : "other";

    public IReadOnlyList<Guid> MessageIds(string arrayProperty)
    {
        if (!Root.TryGetProperty(arrayProperty, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The manifest lacks the expected input list.");
        }

        var ids = new List<Guid>();
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("messageId", out var id) || !Guid.TryParse(id.GetString(), out var parsed))
            {
                throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "An input in the manifest has no message identity.");
            }

            ids.Add(parsed);
        }

        if (ids.Count == 0)
        {
            throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The manifest input list is empty.");
        }

        return ids;
    }

    public Guid? ReportMessageId()
    {
        foreach (var name in new[] { "executionReport", "previousExecutionReport" })
        {
            if (Root.TryGetProperty(name, out var report)
                && report.TryGetProperty("messageId", out var id)
                && Guid.TryParse(id.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
