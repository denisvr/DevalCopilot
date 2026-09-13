using System.Text.RegularExpressions;

namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// Extracts a version-shaped substring (e.g. "2.43.0", "10.0.100", "24.0.7") from already
/// bounded, already-captured probe output. Never called on unbounded text: the caller's
/// process-execution adapter enforces small output caps before this ever runs. Returns null
/// rather than inventing a version when nothing version-shaped is present.
/// </summary>
internal static partial class ProbeOutputVersionParser
{
    public static string? TryExtractVersion(string output)
    {
        var match = VersionPattern().Match(output);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"\d+(?:\.\d+){1,3}")]
    private static partial Regex VersionPattern();
}
