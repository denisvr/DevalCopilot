using System.Text.Json.Nodes;

namespace DevalCopilot.Application.Features.Runs.Policies.FormatRepair;

/// <summary>
/// Places the one fixed, host-authored <c>formatRepairNotice</c> of a CriticalReviewer, Resolver, or
/// CodeReviewer repair manifest. The notice is the only repair-specific content — never the source's
/// response, a parser or validation detail, an artifact path, an attempt identity, or human text —
/// and it sits immediately before the <c>untrustedEvidenceBoundary</c> so it stays in the trusted,
/// host-authored part of the document while every other member keeps its ordinary position and value.
/// </summary>
internal static class ReadOnlyFormatRepairManifest
{
    private const string BoundaryMember = "untrustedEvidenceBoundary";

    public static string InsertNotice(string manifestJson, string notice)
    {
        var root = JsonNode.Parse(manifestJson)!.AsObject();
        var boundaryIndex = root.IndexOf(BoundaryMember);
        root.Insert(boundaryIndex < 0 ? root.Count : boundaryIndex, "formatRepairNotice", notice);
        return root.ToJsonString();
    }
}
