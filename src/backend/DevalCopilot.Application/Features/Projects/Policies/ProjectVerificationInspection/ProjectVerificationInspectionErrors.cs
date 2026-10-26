using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary>The stable, non-echoing refusals of the recorded project verification inspection (ADR-0069). None carries a path, a stored
/// value, an identifier of other work or a rejected request value.</summary>
public static class ProjectVerificationInspectionErrors
{
    public const string NotFoundCode = "project_verification_inspection.not_found";
    public const string BodyNotAllowedCode = "project_verification_inspection.body_not_allowed";
    public const string QueryNotAllowedCode = "project_verification_inspection.query_not_allowed";
    public const string InvalidBeforeExecutionNumberCode = "validation.invalid_before_execution_number";
    public const string InvalidOffsetCode = "validation.invalid_offset";
    public const string InvalidMaxBytesCode = "validation.invalid_max_bytes";
    public const string InvalidStreamCode = "validation.invalid_stream";

    public const string InvalidBeforeExecutionNumberMessage = "The cursor must be a whole number from 1 to 2147483647.";
    public const string InvalidOffsetMessage = "The offset must be a whole number of bytes from 0 to 9007199254740991.";
    public const string InvalidMaxBytesMessage = "The byte cap must be a whole number from 64 to 65536.";

    /// <summary>One fixed answer for an unknown project, an absent or foreign execution and an unsupported stream, so none is
    /// distinguishable and no rejected value is repeated. A present but incoherent execution is never this answer.</summary>
    public static Error NotFound() => Error.NotFound(
        NotFoundCode, "The requested project verification was not found.");

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This request accepts no request body.", ErrorType.Validation);

    public static Error QueryNotAllowed() => new(
        QueryNotAllowedCode, "This request does not accept these query parameters.", ErrorType.Validation);

    /// <summary>The offset lies past the admitted recorded length of the stream; refused before any file is opened.</summary>
    public static Error OffsetBeyondLength() => new ValidationError(
        InvalidOffsetCode, "The offset must not exceed the recorded length of this output.", "FromOffset");
}
