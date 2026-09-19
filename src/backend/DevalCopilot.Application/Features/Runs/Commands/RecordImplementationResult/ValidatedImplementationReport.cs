namespace DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;

/// <summary>
/// A Claude implementation final response that has already passed protocol/schema validation,
/// reduced to exactly the bounded values the recording handler needs.
/// <see cref="ChangedRelativePaths"/> is Claude's own self-report of what it changed — the
/// recording handler never trusts it on its own; it is only ever compared, as an exact set, to
/// independently observed Git evidence before a successful outcome is recorded. The private
/// constructor and the one factory method are the only way to construct this type.
/// </summary>
public sealed class ValidatedImplementationReport
{
    private ValidatedImplementationReport(
        string summary,
        IReadOnlyList<string> changedRelativePaths,
        string implementationNotes,
        string unexpectedDiscoveries,
        string remainingRisks,
        string recommendedVerification)
    {
        Summary = summary;
        ChangedRelativePaths = changedRelativePaths;
        ImplementationNotes = implementationNotes;
        UnexpectedDiscoveries = unexpectedDiscoveries;
        RemainingRisks = remainingRisks;
        RecommendedVerification = recommendedVerification;
    }

    public static ValidatedImplementationReport Create(
        string summary,
        IReadOnlyList<string> changedRelativePaths,
        string implementationNotes,
        string unexpectedDiscoveries,
        string remainingRisks,
        string recommendedVerification) =>
        // Defensively copied, mirroring ValidatedChallengeResolution.Create's own reasoning: a
        // caller-held reference to the original list must never let it be mutated after this
        // "already validated" value is constructed.
        new(summary, changedRelativePaths.ToArray(), implementationNotes, unexpectedDiscoveries, remainingRisks, recommendedVerification);

    /// <summary>The bounded top-level CollaborationMessage.Summary this attempt's ExecutionReport
    /// will carry when the outcome is Implemented.</summary>
    public string Summary { get; }

    /// <summary>Claude's self-reported set of repository-relative paths it changed. Never trusted
    /// on its own: <c>RecordImplementationResultCommandHandler</c> requires this to be an exact
    /// set match against independently observed Git evidence before recording Implemented.
    /// <c>ImplementationResponseParser</c> already rejects any entry that is not repo-relative
    /// (absolute, drive-letter-rooted, or containing a parent-directory traversal segment).</summary>
    public IReadOnlyList<string> ChangedRelativePaths { get; }

    /// <summary>Maps to the ExecutionReport's <c>completedWork</c> field.</summary>
    public string ImplementationNotes { get; }

    /// <summary>Sealed only in the final-response artifact — never duplicated into the ledger.</summary>
    public string UnexpectedDiscoveries { get; }

    /// <summary>Sealed only in the final-response artifact — never duplicated into the ledger.</summary>
    public string RemainingRisks { get; }

    /// <summary>Maps to the ExecutionReport's <c>verification</c> field. A reference for a human
    /// or a later slice to run — never executed by this slice.</summary>
    public string RecommendedVerification { get; }
}
