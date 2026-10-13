using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing refusals of the explicit GitHub repository identity observation (ADR-0041).</summary>
public static class GitHubRepositoryObservationErrors
{
    public const string NotFoundCode = "github_repository_observation.not_found";
    public const string BodyNotAllowedCode = "github_repository_observation.body_not_allowed";

    public static Error NotFound() => Error.NotFound(NotFoundCode, "The requested run was not found.");

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This observation accepts no request body.", ErrorType.Validation);
}
