namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;

/// <summary>
/// Closed states of one explicit check-context observation. <see cref="NotRecorded"/> and <see cref="Unavailable"/> carry no fact and invoke
/// nothing; <see cref="BindingNotRecorded"/> means the immutable binding is genuinely absent and nothing was invoked;
/// <see cref="CliUnavailable"/> means nothing was invoked; <see cref="Unknown"/> covers every unusable outcome and never says why;
/// <see cref="IdentityChanged"/> means a whole valid answer named another opaque repository ID than the binding. Only the final five carry
/// an observation. <see cref="RefNotReturned"/> and <see cref="CommitDifferent"/> expose no context, <see cref="NoContextsReturned"/> means
/// only that GitHub returned no rollup (never that no checks exist), <see cref="LimitExceeded"/> means more than 50 contexts exist and none
/// is shown, and <see cref="ContextsReported"/> carries the bounded collection exactly as returned. None of them says passed, approved,
/// ready or green.
/// </summary>
public enum GitHubCheckContextsObservationStatus
{
    NotRecorded = 0,
    Unavailable = 1,
    BindingNotRecorded = 2,
    CliUnavailable = 3,
    Unknown = 4,
    IdentityChanged = 5,
    RefNotReturned = 6,
    CommitDifferent = 7,
    NoContextsReturned = 8,
    LimitExceeded = 9,
    ContextsReported = 10,
}
