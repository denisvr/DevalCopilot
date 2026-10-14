using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing refusals of the explicit GitHub repository identity binding (ADR-0043). No error carries a submitted value,
/// a stored or remote value, a path, an identifier of other work or an exception.</summary>
public static class GitHubRepositoryIdentityBindingErrors
{
    public const string NotFoundCode = "github_repository_identity_binding.not_found";
    public const string InvalidCode = "github_repository_identity_binding.invalid";
    public const string UnavailableCode = "github_repository_identity_binding.unavailable";
    public const string ConflictCode = "github_repository_identity_binding.conflict";
    public const string CliUnavailableCode = "github_repository_identity_binding.cli_unavailable";
    public const string ObservationUnknownCode = "github_repository_identity_binding.observation_unknown";
    public const string IdentityChangedCode = "github_repository_identity_binding.identity_changed";
    public const string BodyNotAllowedCode = "github_repository_identity_binding.body_not_allowed";

    public static Error NotFound() => Error.NotFound(NotFoundCode, "The requested run was not found.");

    public static Error Invalid() => new(
        InvalidCode,
        "An operation identifier, a lowercase 40-character commit, a target identifier and a repository node identifier are required.",
        ErrorType.Validation);

    public static Error Unavailable() => Error.Conflict(
        UnavailableCode, "This run has no usable recorded GitHub observation target that matches the request.");

    public static Error Conflict() => Error.Conflict(
        ConflictCode, "A different repository identity was already bound for this target.");

    public static Error CliUnavailable() => Error.Conflict(
        CliUnavailableCode, "A current direct GitHub CLI observation is required before an identity can be bound.");

    public static Error ObservationUnknown() => Error.Conflict(
        ObservationUnknownCode, "The repository identity could not be observed reliably.");

    public static Error IdentityChanged() => Error.Conflict(
        IdentityChangedCode, "The observed repository identity differs from the one submitted.");

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This read accepts no request body.", ErrorType.Validation);
}
