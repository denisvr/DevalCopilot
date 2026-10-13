namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryObservation;

/// <summary>
/// Closed states of one repository identity observation. <see cref="NotRecorded"/> and <see cref="Unavailable"/> carry no scope and
/// invoke nothing; <see cref="CliUnavailable"/> means nothing was invoked; <see cref="Unknown"/> covers every unusable outcome and
/// never says why; only <see cref="Observed"/> carries an identity.
/// </summary>
public enum GitHubRepositoryObservationStatus
{
    NotRecorded = 0,
    Unavailable = 1,
    CliUnavailable = 2,
    Unknown = 3,
    Observed = 4,
}
