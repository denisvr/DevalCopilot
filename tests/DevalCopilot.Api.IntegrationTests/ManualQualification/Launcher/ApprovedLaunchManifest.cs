using System.Text.Json;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The owner's approved-launch manifest of one lot B session, read once before anything starts: a closed JSON document naming, per
/// capability, the absolute executable path and SHA-256 digest (and for a Node-hosted script the script path and digest). Unknown,
/// duplicated, missing or mistyped members, an oversized file and any structural problem are refused with a closed code, and the
/// verified set is returned only when every listed file is exactly the approved one. The manifest is data the owner prepared: it
/// grants launch identities, never new commands, arguments or capabilities, and its text is never printed.
/// </summary>
public static class ApprovedLaunchManifest
{
    public const string Option = "--approved-launches";
    public const long MaximumBytes = 32 * 1024;

    private const string RootMember = "approvedLaunches";

    /// <summary>The verified set, or the closed refusal code.</summary>
    public static (ApprovedLaunchSet? Set, string? Refusal) Load(string path)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase))
            {
                return (null, "ManifestPathNotExact");
            }

            var file = new FileInfo(path);
            if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.LinkTarget is not null)
            {
                return (null, "ManifestNotARealFile");
            }

            if (file.Length is < 2 or > MaximumBytes)
            {
                return (null, "ManifestSizeUnexpected");
            }

            var (set, refusal) = Parse(File.ReadAllText(path));
            if (refusal is not null)
            {
                return (null, refusal);
            }

            var unverified = set!.VerifyAll();
            return unverified is null ? (set, null) : (null, unverified);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (null, "ManifestUnreadable");
        }
    }

    /// <summary>Structure only: no approved file is read.</summary>
    public static (ApprovedLaunchSet? Set, string? Refusal) Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, "ManifestShapeInvalid");
            }

            var members = document.RootElement.EnumerateObject().ToArray();
            if (members.Length != 1 || members[0].Name != RootMember || members[0].Value.ValueKind != JsonValueKind.Array)
            {
                return (null, "ManifestShapeInvalid");
            }

            var launches = new List<ApprovedLaunch>();
            foreach (var element in members[0].Value.EnumerateArray())
            {
                var launch = ReadLaunch(element);
                if (launch is null)
                {
                    return (null, "ManifestShapeInvalid");
                }

                launches.Add(launch);
            }

            return ApprovedLaunchSet.TryCreate(launches);
        }
        catch (JsonException)
        {
            return (null, "ManifestShapeInvalid");
        }
    }

    private static ApprovedLaunch? ReadLaunch(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in element.EnumerateObject())
        {
            if (member.Name is not ("capability" or "path" or "sha256" or "scriptPath" or "scriptSha256")
                || member.Value.ValueKind != JsonValueKind.String
                || !values.TryAdd(member.Name, member.Value.GetString() ?? string.Empty))
            {
                return null;
            }
        }

        if (!values.TryGetValue("capability", out var capability)
            || !values.TryGetValue("path", out var path)
            || !values.TryGetValue("sha256", out var sha256)
            || values.ContainsKey("scriptPath") != values.ContainsKey("scriptSha256"))
        {
            return null;
        }

        var script = values.TryGetValue("scriptPath", out var scriptPath)
            ? new ApprovedFile(scriptPath, values["scriptSha256"])
            : null;
        return new ApprovedLaunch(capability, new ApprovedFile(path, sha256), script);
    }
}
