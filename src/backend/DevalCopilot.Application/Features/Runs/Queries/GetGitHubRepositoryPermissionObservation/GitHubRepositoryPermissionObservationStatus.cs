namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;

/// <summary>
/// Closed states of one explicit repository-permission observation. <see cref="NotRecorded"/> and <see cref="Unavailable"/> carry no fact
/// and invoke nothing; <see cref="BindingNotRecorded"/> means the immutable binding is genuinely absent and nothing was invoked;
/// <see cref="CliUnavailable"/> means nothing was invoked; <see cref="Unknown"/> covers every unusable outcome and never says why;
/// <see cref="IdentityChanged"/> means a whole valid answer named another opaque repository ID than the binding. Only the final two carry
/// an observation. <see cref="PermissionNotReturned"/> means only that GitHub returned no viewer permission (never a denial or an account
/// type); <see cref="PermissionReported"/> preserves GitHub's literal role. None of them says allowed, ready, approved or can push.
/// </summary>
public enum GitHubRepositoryPermissionObservationStatus
{
    NotRecorded = 0,
    Unavailable = 1,
    BindingNotRecorded = 2,
    CliUnavailable = 3,
    Unknown = 4,
    IdentityChanged = 5,
    PermissionNotReturned = 6,
    PermissionReported = 7,
}
