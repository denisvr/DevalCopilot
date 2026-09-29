using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.AuthorizeReviewCorrection;

/// <summary>Authorizes one additional review-correction claim for an exhausted-review escalation.
/// <paramref name="Guidance"/> is <see langword="null"/> for the bodyless authorization (its fixed
/// behavior is unchanged); otherwise it is the raw human guidance, validated by the deterministic
/// <c>ReviewCorrectionGuidance.Normalize</c> before anything is persisted.</summary>
public sealed record AuthorizeReviewCorrectionCommand(Guid RunId, Guid EscalationId, string? Guidance = null)
    : IManualTransactionCommand<Result<AuthorizeReviewCorrectionCommandResult>>;
