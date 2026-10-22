using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>The stable, non-echoing refusals of the recorded local execution inspection (ADR-0061). None carries a path, a stored value,
/// an identifier of other work or a rejected request value.</summary>
public static class LocalExecutionInspectionErrors
{
    public const string NotFoundCode = "local_execution_inspection.not_found";
    public const string BodyNotAllowedCode = "local_execution_inspection.body_not_allowed";
    public const string QueryNotAllowedCode = "local_execution_inspection.query_not_allowed";

    /// <summary>One fixed answer for an unknown run, a missing or foreign step, a non-verification step or unsupported stream of a window,
    /// so none is distinguishable and no rejected value is repeated.</summary>
    public static Error NotFound() => Error.NotFound(
        NotFoundCode, "The requested local execution inspection was not found.");

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This request accepts no request body.", ErrorType.Validation);

    public static Error QueryNotAllowed() => new(
        QueryNotAllowedCode, "This request does not accept these query parameters.", ErrorType.Validation);
}
