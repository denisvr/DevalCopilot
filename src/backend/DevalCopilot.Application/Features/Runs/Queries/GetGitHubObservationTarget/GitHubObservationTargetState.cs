namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubObservationTarget;

/// <summary>Closed states of a run's GitHub observation scope. <see cref="Unavailable"/> never says why.</summary>
public enum GitHubObservationTargetState
{
    NotRecorded,
    Unavailable,
    Available,
}
