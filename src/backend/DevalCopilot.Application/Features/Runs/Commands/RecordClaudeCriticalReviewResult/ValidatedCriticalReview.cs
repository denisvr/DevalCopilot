namespace DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;

/// <summary>
/// A Claude critical-review final response that has already passed protocol/schema validation,
/// reduced to exactly the bounded strings <c>CollaborationMessage.Record</c> needs — never both
/// <see cref="Acceptance"/> and a non-empty <see cref="Challenges"/> populated, and never neither;
/// the private constructor and the two factory methods are the only way to construct this type, so
/// that invariant is structural, not merely conventional. Domain still independently validates
/// every field when each message is actually constructed — this is not the only defense.
/// </summary>
public sealed class ValidatedCriticalReview
{
    private ValidatedCriticalReview(ValidatedAcceptance? acceptance, IReadOnlyList<ValidatedChallenge> challenges, string? challengeSetSummary)
    {
        Acceptance = acceptance;
        Challenges = challenges;
        ChallengeSetSummary = challengeSetSummary;
    }

    public static ValidatedCriticalReview ForAcceptance(ValidatedAcceptance acceptance) => new(acceptance, [], null);

    public static ValidatedCriticalReview ForChallenges(IReadOnlyList<ValidatedChallenge> challenges, string challengeSetSummary) =>
        new(null, challenges, challengeSetSummary);

    public ValidatedAcceptance? Acceptance { get; }

    public IReadOnlyList<ValidatedChallenge> Challenges { get; }

    /// <summary>The provider's own bounded, safe summary of the challenge set as a whole. Never
    /// turned into its own <see cref="DevalCopilot.Domain.Features.Runs.CollaborationMessage"/> —
    /// there is no message type for it — but preserved for the terminal RunEvent's bounded
    /// payload.</summary>
    public string? ChallengeSetSummary { get; }

    public bool IsAcceptance => Acceptance is not null;
}

/// <summary>Mirrors <c>ValidatedProposal</c> exactly.</summary>
public sealed record ValidatedAcceptance(string Summary, string StructuredContentJson);

/// <summary>Mirrors <c>ValidatedProposal</c> exactly — one already-validated Challenge item.</summary>
public sealed record ValidatedChallenge(string Summary, string StructuredContentJson);
