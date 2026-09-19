namespace DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;

/// <summary>
/// A Codex implementation-review final response that has already passed protocol/schema
/// validation — a strict discriminated Approved-or-ChangesRequested shape, reduced to exactly the
/// bounded strings <c>CollaborationMessage.Record</c> needs. The private constructor and the two
/// factory methods are the only way to construct this type: <see cref="IsApproved"/> and
/// <see cref="Findings"/> can never disagree (Approved always has zero findings, ChangesRequested
/// always has one-to-ten). Domain still independently validates every field when each message is
/// actually constructed — this is not the only defense. Mirrors <c>ValidatedChallengeResolution</c>
/// exactly, one shape further down the collaboration protocol.
/// </summary>
public sealed class ValidatedImplementationReview
{
    private ValidatedImplementationReview(
        bool isApproved, string summary, string? rationale, string? residualRisks, IReadOnlyList<ValidatedReviewFinding> findings)
    {
        IsApproved = isApproved;
        Summary = summary;
        Rationale = rationale;
        ResidualRisks = residualRisks;
        Findings = findings;
    }

    public static ValidatedImplementationReview CreateApproved(string summary, string rationale, string residualRisks) =>
        new(true, summary, rationale, residualRisks, []);

    public static ValidatedImplementationReview CreateChangesRequested(string summary, IReadOnlyList<ValidatedReviewFinding> findings) =>
        // Defensively copied for the same reason ValidatedChallengeResolution.Create copies its
        // own list — a caller must never be able to mutate this "already validated" collection
        // after construction.
        new(false, summary, null, null, findings.ToArray());

    public bool IsApproved { get; }

    public string Summary { get; }

    public string? Rationale { get; }

    public string? ResidualRisks { get; }

    /// <summary>Empty when <see cref="IsApproved"/>; one-to-ten entries otherwise.
    /// <see cref="ImplementationReviewResponseParser"/> enforces that shape when it builds this
    /// value from a provider response, but this type itself makes no such guarantee (a caller
    /// could construct one directly) — <c>RecordImplementationReviewResultCommandHandler</c>
    /// independently re-verifies cardinality before ever trusting this collection.</summary>
    public IReadOnlyList<ValidatedReviewFinding> Findings { get; }
}

/// <summary>One already-validated material finding. <see cref="AffectedRelativePath"/> is the one
/// field this type carries that is never recorded to the durable collaboration ledger — it is
/// surfaced only through the sealed final-response artifact, exactly like every other bounded
/// field this protocol keeps out of durable structured content.</summary>
public sealed record ValidatedReviewFinding(
    string Severity, string Category, string Summary, string Evidence, string RequiredChange, string? AffectedRelativePath);
