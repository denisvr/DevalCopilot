namespace DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;

/// <summary>
/// A Codex challenge-resolution final response that has already passed protocol/schema
/// validation — one already-validated <see cref="ValidatedDecision"/> per input Challenge and
/// exactly one already-validated <see cref="ValidatedRevisedProposal"/>, reduced to exactly the
/// bounded strings <c>CollaborationMessage.Record</c> needs. The private constructor and the one
/// factory method are the only way to construct this type. Domain still independently validates
/// every field when each message is actually constructed — this is not the only defense.
/// </summary>
public sealed class ValidatedChallengeResolution
{
    private ValidatedChallengeResolution(string summary, IReadOnlyList<ValidatedDecision> decisions, ValidatedRevisedProposal revisedProposal)
    {
        Summary = summary;
        Decisions = decisions;
        RevisedProposal = revisedProposal;
    }

    public static ValidatedChallengeResolution Create(
        string summary, IReadOnlyList<ValidatedDecision> decisions, ValidatedRevisedProposal revisedProposal) =>
        // Defensively copied: the caller's own list must never be mutable-by-reference from
        // outside this type after construction — a caller that kept a reference to the list it
        // passed in (e.g. a hand-built ValidatedChallengeResolution in a test, or any future
        // caller that isn't ChallengeResolutionResponseParser) could otherwise append or remove
        // entries after Create returns, silently invalidating every downstream count/uniqueness
        // check the recording handler performs against this "already validated" value.
        new(summary, decisions.ToArray(), revisedProposal);

    /// <summary>The provider's own bounded, safe summary of the resolution as a whole. Never
    /// turned into its own <see cref="DevalCopilot.Domain.Features.Runs.CollaborationMessage"/> —
    /// there is no message type for it — but preserved for the terminal RunEvent's bounded
    /// payload, mirroring <c>ValidatedCriticalReview.ChallengeSetSummary</c>.</summary>
    public string Summary { get; }

    /// <summary>Intended to hold exactly one entry per input Challenge — never fewer, never
    /// more, never a duplicate or foreign identifier. <see cref="ChallengeResolutionResponseParser"/>
    /// enforces that shape when it builds this value from a provider response, but this type
    /// itself makes no such guarantee (a caller could construct one directly with a duplicated
    /// or incomplete set) — <c>RecordChallengeResolutionResultCommandHandler</c> independently
    /// re-verifies count, uniqueness, and set-equality against the attempt's own persisted
    /// Challenge set before ever trusting this collection, never relying solely on the
    /// parser.</summary>
    public IReadOnlyList<ValidatedDecision> Decisions { get; }

    public ValidatedRevisedProposal RevisedProposal { get; }
}

/// <summary>One already-validated Decision, identified by the exact Challenge message it
/// resolves.</summary>
public sealed record ValidatedDecision(Guid ChallengeMessageId, string Summary, string StructuredContentJson);

/// <summary>Mirrors <c>ValidatedProposal</c> exactly — the one already-validated revised
/// Proposal this attempt produces, replying to the original Proposal.</summary>
public sealed record ValidatedRevisedProposal(string Summary, string StructuredContentJson);
