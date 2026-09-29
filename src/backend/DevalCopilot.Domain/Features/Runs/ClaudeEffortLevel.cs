namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed set of Claude CLI <c>--effort</c> levels this product lets the owner request. The
/// levels come from the documented Claude CLI reference; membership is a request-syntax contract
/// only — the provider may reject or silently adjust a request, so it is never an observed or
/// effective effort.
/// </summary>
public static class ClaudeEffortLevel
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    public static IReadOnlyList<string> Supported { get; } = [Low, Medium, High];

    /// <summary>Exact, case-sensitive membership: no trimming, normalization, or fuzzy matching.</summary>
    public static bool IsSupported(string? value) => value is not null && Supported.Contains(value, StringComparer.Ordinal);
}
