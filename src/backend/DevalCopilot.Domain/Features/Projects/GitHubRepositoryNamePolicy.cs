namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// The conservative ASCII subset of GitHub repository names this product supports (ADR-0039, ADR-0040), not the full GitHub
/// naming policy. The owner is 1 to 39 ASCII letters, digits and hyphens, beginning and ending alphanumeric; the repository is
/// 1 to 100 ASCII letters, digits, dots, underscores and hyphens, beginning alphanumeric (so a dot segment is impossible). The
/// match is ordinal and never trims, decodes or normalizes its input. Pure; it knows no URL, host, Git or filesystem concept.
/// </summary>
public static class GitHubRepositoryNamePolicy
{
    public const int MaximumOwnerLength = 39;

    public const int MaximumRepositoryLength = 100;

    public static bool IsOwner(string? owner) =>
        owner is { Length: >= 1 and <= MaximumOwnerLength }
        && IsAsciiAlphanumeric(owner[0])
        && IsAsciiAlphanumeric(owner[^1])
        && owner.All(character => IsAsciiAlphanumeric(character) || character == '-');

    public static bool IsRepository(string? repository) =>
        repository is { Length: >= 1 and <= MaximumRepositoryLength }
        && IsAsciiAlphanumeric(repository[0])
        && repository.All(character => IsAsciiAlphanumeric(character) || character is '.' or '_' or '-');

    private static bool IsAsciiAlphanumeric(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
}
