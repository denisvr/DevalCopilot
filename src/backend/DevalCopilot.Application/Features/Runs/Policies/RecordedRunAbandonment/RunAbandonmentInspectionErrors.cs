using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;

/// <summary>The stable, non-echoing refusals of the recorded abandonment inspection (ADR-0071). None carries a path, a stored value, an
/// identifier of other work or a rejected request value.</summary>
public static class RunAbandonmentInspectionErrors
{
    public const string NotFoundCode = "run_abandonment_inspection.not_found";
    public const string BodyNotAllowedCode = "run_abandonment_inspection.body_not_allowed";
    public const string QueryNotAllowedCode = "run_abandonment_inspection.query_not_allowed";
    public const string InvalidRunCode = "validation.invalid_run";

    public const string InvalidRunMessage = "A run identifier is required.";

    /// <summary>One fixed answer for an unknown run and a run whose registered project is absent, so neither is distinguishable and no
    /// rejected value is repeated. A run with present but damaged abandonment evidence is never this answer.</summary>
    public static Error NotFound() => Error.NotFound(
        NotFoundCode, "The requested run abandonment inspection was not found.");

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This request accepts no request body.", ErrorType.Validation);

    public static Error QueryNotAllowed() => new(
        QueryNotAllowedCode, "This request does not accept these query parameters.", ErrorType.Validation);
}
