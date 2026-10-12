using DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// Admits exactly three literal spellings of a GitHub repository and nothing else: <c>https://github.com/owner/repository</c>,
/// <c>git@github.com:owner/repository</c> and <c>ssh://git@github.com/owner/repository</c>, each with one optional terminal
/// <c>.git</c> removed. The match is ordinal and the input is never trimmed, decoded or normalized, so a credential, port,
/// escape, whitespace or control character, query, fragment, extra or empty segment, trailing slash, alternate user, host
/// or protocol, and a local path are all simply not admitted. The owner and repository rules are a conservative supported
/// subset, not the full GitHub naming policy. The raw value stays inside this type: a rejection exposes nothing.
/// </summary>
internal static class GitHubOriginUrlParser
{
    private const int MaxOwnerLength = 39;
    private const int MaxRepositoryLength = 100;
    private const string GitSuffix = ".git";

    private static readonly string[] Prefixes =
        ["https://github.com/", "git@github.com:", "ssh://git@github.com/"];

    public static GitHubOriginTarget? TryParse(string rawValue)
    {
        var path = StripPrefix(rawValue);
        if (path is null)
        {
            return null;
        }

        var separator = path.IndexOf('/');
        if (separator < 0 || separator != path.LastIndexOf('/'))
        {
            return null;
        }

        var owner = path[..separator];
        var repository = path[(separator + 1)..];
        if (repository.EndsWith(GitSuffix, StringComparison.Ordinal))
        {
            repository = repository[..^GitSuffix.Length];
        }

        return IsOwner(owner) && IsRepository(repository) ? new GitHubOriginTarget(owner, repository) : null;
    }

    private static string? StripPrefix(string rawValue)
    {
        foreach (var prefix in Prefixes)
        {
            if (rawValue.StartsWith(prefix, StringComparison.Ordinal))
            {
                return rawValue[prefix.Length..];
            }
        }

        return null;
    }

    private static bool IsOwner(string owner) =>
        owner.Length is >= 1 and <= MaxOwnerLength
        && IsAsciiAlphanumeric(owner[0])
        && IsAsciiAlphanumeric(owner[^1])
        && owner.All(character => IsAsciiAlphanumeric(character) || character == '-');

    private static bool IsRepository(string repository) =>
        repository.Length is >= 1 and <= MaxRepositoryLength
        && IsAsciiAlphanumeric(repository[0])
        && repository.All(character => IsAsciiAlphanumeric(character) || character is '.' or '_' or '-');

    private static bool IsAsciiAlphanumeric(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
}
