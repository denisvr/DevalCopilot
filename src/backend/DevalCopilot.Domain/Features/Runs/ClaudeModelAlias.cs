namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed set of Claude CLI <c>--model</c> aliases this product lets the owner request. The
/// aliases come from the documented Claude CLI reference; membership here is a request-syntax
/// contract only — it never proves that an alias is enabled for the signed-in account, and it is
/// never an observed or effective model.
/// </summary>
public static class ClaudeModelAlias
{
    public const string Sonnet = "sonnet";
    public const string Opus = "opus";
    public const string Haiku = "haiku";

    public static IReadOnlyList<string> Supported { get; } = [Sonnet, Opus, Haiku];

    /// <summary>Exact, case-sensitive membership: no trimming, normalization, or fuzzy matching.</summary>
    public static bool IsSupported(string? value) => value is not null && Supported.Contains(value, StringComparer.Ordinal);
}
