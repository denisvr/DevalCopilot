using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing refusals of the explicit GitHub observation scope selection (ADR-0040). No error carries a submitted
/// value, a stored value, a path, an identifier of other work or an exception.</summary>
public static class GitHubObservationTargetErrors
{
    public const string NotFoundCode = "github_observation_target.not_found";
    public const string UnavailableCode = "github_observation_target.unavailable";
    public const string ConflictCode = "github_observation_target.conflict";
    public const string InvalidCode = "validation.invalid";

    public static Error NotFound() => Error.NotFound(NotFoundCode, "The requested run was not found.");

    public static Error Unavailable() => Error.Conflict(
        UnavailableCode, "This run has no usable completed local delivery that matches the request.");

    public static Error Conflict() => Error.Conflict(
        ConflictCode, "A different GitHub observation target was already recorded for this delivery.");

    public static Error Invalid() => Error.Failure(
        InvalidCode,
        "An operation identifier, a lowercase 40-character commit and a supported GitHub owner and repository are required.");
}
