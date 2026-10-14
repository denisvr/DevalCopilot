using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing refusals of the explicit GitHub check-run output inspection (ADR-0047).</summary>
public static class GitHubCheckRunOutputObservationErrors
{
    public const string NotFoundCode = "github_check_run_output_observation.not_found";
    public const string InvalidRequestCode = "github_check_run_output_observation.invalid_request";
    public const string BodyNotAllowedCode = "github_check_run_output_observation.body_not_allowed";

    public static Error NotFound() => Error.NotFound(NotFoundCode, "The requested run was not found.");

    public static Error InvalidRequest() => new(
        InvalidRequestCode, "The request must carry exactly one valid checkRunId query value.", ErrorType.Validation);

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This inspection accepts no request body.", ErrorType.Validation);
}
