namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;

/// <summary>
/// Closed states of one explicit check-run output inspection. <see cref="NotRecorded"/> and <see cref="Unavailable"/> carry no fact and
/// invoke nothing; <see cref="BindingNotRecorded"/> means the immutable binding is genuinely absent and nothing was invoked;
/// <see cref="CliUnavailable"/> means the current CLI evidence was not usable before a call (nothing, or no second call, was made);
/// <see cref="Unknown"/> covers every unusable outcome and never says why; <see cref="IdentityChanged"/> means a whole valid answer named
/// another opaque repository ID than the binding. Only the final five carry an observation. <see cref="RefNotReturned"/> and
/// <see cref="CommitDifferent"/> expose no check, <see cref="CheckRunUnavailable"/> means the requested check was not proved to belong to
/// the bound repository and recorded commit (or its association changed between the two phases), <see cref="OutputNotReturned"/> means
/// the admitted check carried no title, summary or text, and <see cref="OutputReported"/> carries the output exactly as admitted. None of
/// them says passed, approved, ready or green.
/// </summary>
public enum GitHubCheckRunOutputObservationStatus
{
    NotRecorded = 0,
    Unavailable = 1,
    BindingNotRecorded = 2,
    CliUnavailable = 3,
    Unknown = 4,
    IdentityChanged = 5,
    RefNotReturned = 6,
    CommitDifferent = 7,
    CheckRunUnavailable = 8,
    OutputNotReturned = 9,
    OutputReported = 10,
}
