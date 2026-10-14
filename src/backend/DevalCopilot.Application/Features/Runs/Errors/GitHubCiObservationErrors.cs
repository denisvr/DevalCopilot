using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing refusals of the explicit GitHub commit check observation (ADR-0044).</summary>
public static class GitHubCiObservationErrors
{
    public const string NotFoundCode = "github_ci_observation.not_found";
    public const string BodyNotAllowedCode = "github_ci_observation.body_not_allowed";

    public static Error NotFound() => Error.NotFound(NotFoundCode, "The requested run was not found.");

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This observation accepts no request body.", ErrorType.Validation);
}
