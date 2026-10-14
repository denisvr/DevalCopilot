namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryIdentityBinding;

/// <summary>Closed states of a run's GitHub repository identity binding. <see cref="Unavailable"/> never says why.</summary>
public enum GitHubRepositoryIdentityBindingState
{
    NotRecorded,
    Unavailable,
    Available,
}
