namespace DevalCopilot.Application.Features.Runs.Queries.GetSealedAgentArtifactWindow;

/// <summary>The closed, honest shape of one requested sealed Agent-attempt artifact window. Never
/// inferred from field nullability alone — always read this discriminator first.</summary>
public enum SealedAgentArtifactWindowStatus
{
    /// <summary>Bytes returned normally from a verified sealed artifact.</summary>
    Ok,

    /// <summary>The message is not <c>ProviderObserved</c> — a legitimate Human/Orchestrator/
    /// Simulated message never has Agent artifact evidence.</summary>
    NoAgentEvidence,

    /// <summary>The message is <c>ProviderObserved</c> but its evidence could not be trusted:
    /// <c>AttemptId</c> is null, no matching Agent-kind <c>Attempt</c> row exists, or its
    /// role/provider does not match the message's own actor.</summary>
    AttemptLinkBroken,

    /// <summary>The attempt was selected directly from the run history (not through a message) and its
    /// persisted role, provider, response contract, or assignment could not be proven coherent, so no
    /// evidence is disclosed. Never produced by the message-linked route.</summary>
    AttemptIdentityInvalid,

    /// <summary>The requested purpose is outside the closed four-purpose Agent-artifact allowlist
    /// (context manifest, stdout, stderr, final response) — defense in depth; the API boundary
    /// already restricts the route to these four values.</summary>
    PurposeNotAllowlisted,

    /// <summary>No <c>Artifact</c> row exists for this exact attempt, run, and purpose.</summary>
    ArtifactNotFound,

    /// <summary>An <c>Artifact</c> row exists, but its sealed file could not be found on disk.</summary>
    Missing,

    /// <summary>An <c>Artifact</c> row exists, but its sealed file's actual length or hash no
    /// longer matches its durable metadata. Never presented as trustworthy evidence.</summary>
    IntegrityMismatch,
}

/// <summary>
/// One bounded window of a sealed Agent-attempt artifact's own verified text — never a complete
/// transcript, and never returned unless the entire sealed file first passed its length/hash
/// check. <see cref="Truncated"/> mirrors the artifact's own durable capture truncation fact and
/// is populated only for <see cref="SealedAgentArtifactWindowStatus.Ok"/>.
/// </summary>
public sealed record GetSealedAgentArtifactWindowQueryResult(
    SealedAgentArtifactWindowStatus Status, string Text, long NextOffset, long TotalLengthSoFar, bool? Truncated)
{
    public static GetSealedAgentArtifactWindowQueryResult NoAgentEvidence(long fromOffset) =>
        new(SealedAgentArtifactWindowStatus.NoAgentEvidence, string.Empty, fromOffset, 0, null);

    public static GetSealedAgentArtifactWindowQueryResult AttemptLinkBroken(long fromOffset) =>
        new(SealedAgentArtifactWindowStatus.AttemptLinkBroken, string.Empty, fromOffset, 0, null);

    public static GetSealedAgentArtifactWindowQueryResult AttemptIdentityInvalid(long fromOffset) =>
        new(SealedAgentArtifactWindowStatus.AttemptIdentityInvalid, string.Empty, fromOffset, 0, null);

    public static GetSealedAgentArtifactWindowQueryResult PurposeNotAllowlisted(long fromOffset) =>
        new(SealedAgentArtifactWindowStatus.PurposeNotAllowlisted, string.Empty, fromOffset, 0, null);

    public static GetSealedAgentArtifactWindowQueryResult ArtifactNotFound(long fromOffset) =>
        new(SealedAgentArtifactWindowStatus.ArtifactNotFound, string.Empty, fromOffset, 0, null);
}
