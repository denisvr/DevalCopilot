using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing errors of direct human guidance on a mutation request and its dispatch consistency.
/// No error carries submitted or stored guidance text, a manifest, or a provider payload.</summary>
public static class DirectHumanGuidanceErrors
{
    public const string InvalidCode = "agent_attempts.direct_guidance_invalid";
    public const string UnavailableAtExhaustionCode = "agent_attempts.direct_guidance_unavailable";
    public const string SnapshotMismatchCode = "agent_attempts.direct_guidance_mismatch";

    public static string InvalidMessage { get; } =
        $"The guidance must be non-blank text of at most {Domain.Features.Runs.DirectHumanGuidance.MaximumLength} characters "
        + "without control characters or unsafe content.";

    public static Error Invalid() => Error.Failure(InvalidCode, InvalidMessage);

    /// <summary>The ordinary correction budget is exhausted: direct guidance is available only within it. The request
    /// is refused whole, creating no escalation and consuming no authorization.</summary>
    public static Error UnavailableAtExhaustion() => Error.Conflict(
        UnavailableAtExhaustionCode,
        "Direct guidance is available only within the ordinary review-correction budget; this request was not accepted.");

    /// <summary>The fresh persisted snapshot disagrees with the expected one (or with the sealed context).</summary>
    public static Error SnapshotMismatch() => Error.Conflict(
        SnapshotMismatchCode, "The attempt's recorded direct guidance does not match the expected snapshot.");
}
