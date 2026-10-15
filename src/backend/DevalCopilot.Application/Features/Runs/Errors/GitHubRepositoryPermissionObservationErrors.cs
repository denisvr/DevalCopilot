using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing refusals of the explicit GitHub repository permission observation (ADR-0049). No error carries a submitted,
/// stored or remote value.</summary>
public static class GitHubRepositoryPermissionObservationErrors
{
    public const string NotFoundCode = "github_repository_permission_observation.not_found";
    public const string InvalidRequestCode = "github_repository_permission_observation.invalid_request";
    public const string BodyNotAllowedCode = "github_repository_permission_observation.body_not_allowed";

    public static Error NotFound() => Error.NotFound(NotFoundCode, "The requested run was not found.");

    public static Error InvalidRequest() => new(
        InvalidRequestCode, "This observation accepts no query.", ErrorType.Validation);

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This observation accepts no request body.", ErrorType.Validation);
}
