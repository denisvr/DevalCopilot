using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationOutputWindow;

public static class GetAgentAttemptVerificationOutputWindowErrors
{
    public const string InvalidOffsetCode = "validation.invalid_offset";

    public const string InvalidOffsetMessage = "The offset must be a whole number of bytes from 0 to 9007199254740991.";

    public const string InvalidMaxBytesCode = "validation.invalid_max_bytes";

    public const string InvalidMaxBytesMessage = "The byte cap must be a whole number from 64 to 65536.";

    public const string InvalidStreamCode = "validation.invalid_stream";

    public static Error InvalidOffset() => new ValidationError(
        InvalidOffsetCode, InvalidOffsetMessage, nameof(GetAgentAttemptVerificationOutputWindowQuery.FromOffset));

    public static Error InvalidMaxBytes() => new ValidationError(
        InvalidMaxBytesCode, InvalidMaxBytesMessage, nameof(GetAgentAttemptVerificationOutputWindowQuery.MaxBytes));

    /// <summary>The offset lies past the admitted recorded length of the stream; refused before any file is opened.</summary>
    public static Error OffsetBeyondLength() => new ValidationError(
        InvalidOffsetCode,
        "The offset must not exceed the recorded length of this output.",
        nameof(GetAgentAttemptVerificationOutputWindowQuery.FromOffset));
}
