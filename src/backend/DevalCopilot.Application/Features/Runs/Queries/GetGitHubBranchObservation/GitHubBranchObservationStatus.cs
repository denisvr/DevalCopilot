namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;

/// <summary>
/// Closed states of one remote branch observation. <see cref="NotRecorded"/> and <see cref="Unavailable"/> carry no scope and invoke
/// nothing; <see cref="CliUnavailable"/> means nothing was invoked; <see cref="Unknown"/> covers every unusable outcome and never says
/// why. <see cref="RefNotReturned"/> means only that GitHub returned no ref for this observation; it never implies nonexistence or
/// permission. <see cref="CommitMatched"/> and <see cref="CommitDifferent"/> compare the remote tip with the recorded local commit.
/// </summary>
public enum GitHubBranchObservationStatus
{
    NotRecorded = 0,
    Unavailable = 1,
    CliUnavailable = 2,
    Unknown = 3,
    RefNotReturned = 4,
    CommitMatched = 5,
    CommitDifferent = 6,
}
