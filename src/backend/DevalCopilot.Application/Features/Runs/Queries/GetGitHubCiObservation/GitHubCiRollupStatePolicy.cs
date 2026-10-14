namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCiObservation;

/// <summary>The one admission rule for GitHub's <c>StatusState</c> of a commit's <c>statusCheckRollup</c>: exactly the five literal values the
/// GraphQL reference defines. Nothing is normalized, translated or defaulted; an unknown value is never admitted. Pure.</summary>
public static class GitHubCiRollupStatePolicy
{
    public static bool IsRollupState(string? value) =>
        value is "ERROR" or "EXPECTED" or "FAILURE" or "PENDING" or "SUCCESS";
}
