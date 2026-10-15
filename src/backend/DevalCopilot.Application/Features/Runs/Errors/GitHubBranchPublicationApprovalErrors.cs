using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing refusals of the explicit GitHub branch publication approval (ADR-0048). No error carries a submitted,
/// stored or remote value, a path, an identifier of other work or an exception.</summary>
public static class GitHubBranchPublicationApprovalErrors
{
    public const string InvalidRequestCode = "github_branch_publication_approval.invalid_request";
    public const string BodyNotAllowedCode = "github_branch_publication_approval.body_not_allowed";
    public const string BodyTooLargeCode = "github_branch_publication_approval.body_too_large";
    public const string NotFoundCode = "github_branch_publication_approval.not_found";
    public const string UnavailableCode = "github_branch_publication_approval.unavailable";
    public const string ConflictCode = "github_branch_publication_approval.conflict";

    public static Error InvalidRequest() => new(
        InvalidRequestCode,
        "A request with exactly the seven expected string members and no query is required.",
        ErrorType.Validation);

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This read accepts no request body.", ErrorType.Validation);

    public static Error BodyTooLarge() => new(
        BodyTooLargeCode, "The request body exceeds the supported size.", ErrorType.Validation);

    public static Error NotFound() => Error.NotFound(NotFoundCode, "The requested run was not found.");

    public static Error Unavailable() => Error.Conflict(
        UnavailableCode, "This run has no usable recorded GitHub target and binding that match the request.");

    public static Error Conflict() => Error.Conflict(
        ConflictCode, "A different branch publication approval was already recorded for this run.");
}
