using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;

/// <summary>The stable, non-echoing refusals of the recorded local-commit inspection (ADR-0070). None carries a path, a stored value, an
/// identifier of other work or a rejected request value.</summary>
public static class LocalCommitInspectionErrors
{
    public const string NotFoundCode = "local_commit_inspection.not_found";
    public const string BodyNotAllowedCode = "local_commit_inspection.body_not_allowed";
    public const string QueryNotAllowedCode = "local_commit_inspection.query_not_allowed";
    public const string InvalidRunCode = "validation.invalid_run";

    public const string InvalidRunMessage = "A run identifier is required.";

    /// <summary>One fixed answer for an unknown run and a run whose registered project is absent, so neither is distinguishable and no
    /// rejected value is repeated. A run with a present but damaged operation is never this answer.</summary>
    public static Error NotFound() => Error.NotFound(
        NotFoundCode, "The requested local commit inspection was not found.");

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This request accepts no request body.", ErrorType.Validation);

    public static Error QueryNotAllowed() => new(
        QueryNotAllowedCode, "This request does not accept these query parameters.", ErrorType.Validation);
}
