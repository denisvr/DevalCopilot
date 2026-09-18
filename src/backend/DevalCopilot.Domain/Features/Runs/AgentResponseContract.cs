namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed shape of durable collaboration fact an <see cref="AttemptKind.Agent"/> attempt is
/// expected to produce. Distinct from <see cref="CollaborationMessageType"/> (the single message
/// type an Agent <em>attempt's own claimed intent</em> expects — always <c>Proposal</c> today, for
/// both combinations) because a critical-review attempt's real output is a union of one Acceptance
/// or one-to-several Challenge messages, which is never represented by encoding that union as a
/// bare <see langword="null"/> or by silently overloading a single expected-message-type field.
/// Every combination below is fixed by exactly one Domain factory — never caller-selected, never a
/// loosely validated bag of nullable arguments.
/// </summary>
public enum AgentResponseContract
{
    /// <summary>Codex + Planner. The attempt's real output is exactly one
    /// <see cref="CollaborationMessageType.Proposal"/>.</summary>
    Proposal = 0,

    /// <summary>ClaudeCode + CriticalReviewer. The attempt's real output is exactly one
    /// <see cref="CollaborationMessageType.Acceptance"/>, or one to five
    /// <see cref="CollaborationMessageType.Challenge"/> messages — never both, never zero.</summary>
    CriticalReview = 1,

    /// <summary>Codex + Resolver. The attempt's real output is one
    /// <see cref="CollaborationMessageType.Decision"/> per input Challenge, plus exactly one
    /// revised <see cref="CollaborationMessageType.Proposal"/> replying to the original
    /// Proposal.</summary>
    ChallengeResolution = 2,
}
