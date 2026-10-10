namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>What the session can prove about one Git directory without writing to it: the commit, the HEAD reference, every
/// reference and object name, the staged index entries (mode, object name, stage, path: independent of stat-cache refreshes), and
/// one hash over the path and content hash of every file outside the Git directory, tracked or not.</summary>
public sealed record SourceSnapshot(
    string Head,
    string HeadReference,
    string References,
    string IndexEntries,
    string FilesSha256)
{
    /// <summary>The closed names of what differs from <paramref name="baseline"/>. A reference the host itself was asked to create
    /// (<paramref name="allowedExtraReference"/>) is the only addition tolerated; a moved or removed reference never is.</summary>
    public IReadOnlyList<string> DifferencesFrom(SourceSnapshot baseline, string? allowedExtraReference = null)
    {
        var differences = new List<string>();
        AddIf(differences, "Head", Head != baseline.Head);
        AddIf(differences, "HeadReference", HeadReference != baseline.HeadReference);
        AddIf(differences, "Index", IndexEntries != baseline.IndexEntries);
        AddIf(differences, "Files", FilesSha256 != baseline.FilesSha256);
        var current = Lines(References);
        var expected = Lines(baseline.References);
        var unexpectedAddition = current.Except(expected).Any(line =>
            allowedExtraReference is null || !line.StartsWith(allowedExtraReference + " ", StringComparison.Ordinal));
        AddIf(differences, "References", expected.Except(current).Any() || unexpectedAddition);
        return differences;
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static void AddIf(List<string> differences, string name, bool differs)
    {
        if (differs)
        {
            differences.Add(name);
        }
    }
}
