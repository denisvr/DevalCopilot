namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryObservation;

/// <summary>
/// The admission rule for the opaque GitHub repository node identifier observed by ADR-0041: 1 to 256 ASCII letters, digits and
/// the characters <c>_ - / + =</c>. The value is never decoded, trimmed or interpreted. Pure.
/// </summary>
public static class GitHubRepositoryNodeIdPolicy
{
    public const int MaximumLength = 256;

    public static bool IsNodeId(string? value) =>
        value is { Length: >= 1 and <= MaximumLength } && value.All(IsAllowed);

    private static bool IsAllowed(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '/' or '+' or '=';
}
