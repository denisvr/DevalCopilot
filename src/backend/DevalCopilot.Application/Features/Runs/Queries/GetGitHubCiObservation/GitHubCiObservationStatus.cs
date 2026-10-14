namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCiObservation;

/// <summary>
/// Closed states of one explicit check-summary observation. <see cref="NotRecorded"/> and <see cref="Unavailable"/> carry no fact and
/// invoke nothing; <see cref="BindingNotRecorded"/> means the immutable binding is genuinely absent and nothing was invoked;
/// <see cref="CliUnavailable"/> means nothing was invoked; <see cref="Unknown"/> covers every unusable outcome and never says why;
/// <see cref="IdentityChanged"/> means a whole valid answer named another opaque repository ID than the binding. Only the final four
/// carry an observation. <see cref="CommitDifferent"/> and <see cref="RefNotReturned"/> expose no check state, and
/// <see cref="NoStatusReported"/> means only that GitHub returned no rollup, never that no checks exist. None of them says passed,
/// approved, ready or green; <see cref="StatusReported"/> preserves GitHub's literal state.
/// </summary>
public enum GitHubCiObservationStatus
{
    NotRecorded = 0,
    Unavailable = 1,
    BindingNotRecorded = 2,
    CliUnavailable = 3,
    Unknown = 4,
    IdentityChanged = 5,
    RefNotReturned = 6,
    CommitDifferent = 7,
    NoStatusReported = 8,
    StatusReported = 9,
}
