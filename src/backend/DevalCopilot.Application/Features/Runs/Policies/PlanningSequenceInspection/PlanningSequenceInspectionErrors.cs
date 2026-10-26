using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>The stable, non-echoing refusals of the recorded planning sequence inspection (ADR-0068). None carries a path, a stored
/// value, an identifier of other work or a rejected request value.</summary>
public static class PlanningSequenceInspectionErrors
{
    public const string NotFoundCode = "planning_sequence_inspection.not_found";
    public const string BodyNotAllowedCode = "planning_sequence_inspection.body_not_allowed";
    public const string QueryNotAllowedCode = "planning_sequence_inspection.query_not_allowed";

    /// <summary>One fixed answer for an unknown run and for a missing or foreign step, so none is distinguishable and no rejected value is
    /// repeated.</summary>
    public static Error NotFound() => Error.NotFound(
        NotFoundCode, "The requested planning sequence inspection was not found.");

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This request accepts no request body.", ErrorType.Validation);

    public static Error QueryNotAllowed() => new(
        QueryNotAllowedCode, "This request does not accept these query parameters.", ErrorType.Validation);
}
