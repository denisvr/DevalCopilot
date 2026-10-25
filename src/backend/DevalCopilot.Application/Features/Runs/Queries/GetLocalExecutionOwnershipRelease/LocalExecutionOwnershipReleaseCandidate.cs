using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;

/// <summary>The one release candidate (ADR-0065): the complete proved historical tuple and the exact versioned confirmation the Human must
/// submit, valid until <see cref="ConfirmationExpiresAtUtc"/> (at most two minutes after the read). The confirmation is an exact-intent
/// check, not a credential or process proof.</summary>
public sealed record LocalExecutionOwnershipReleaseCandidate(
    LocalExecutionOwnershipReleaseFacts Facts,
    int ConfirmationVersion,
    string ConfirmationSha256,
    DateTimeOffset ConfirmationExpiresAtUtc);
