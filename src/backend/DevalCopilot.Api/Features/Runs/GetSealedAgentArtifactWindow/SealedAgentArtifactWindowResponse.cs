namespace DevalCopilot.Api.Features.Runs.GetSealedAgentArtifactWindow;

/// <summary>
/// One bounded, verified text window of a sealed Agent-attempt artifact. <c>Status</c> is
/// <c>"Ok"</c> (content present), <c>"NoAgentEvidence"</c>, <c>"AttemptLinkBroken"</c>,
/// <c>"PurposeNotAllowlisted"</c>, <c>"ArtifactNotFound"</c>, <c>"Missing"</c>, or
/// <c>"IntegrityMismatch"</c> — every non-<c>"Ok"</c> status carries an empty <c>Text</c> and a
/// zero <c>TotalLengthSoFar</c>, never partial or fabricated content. Never carries a storage
/// path, content hash, or any other raw diagnostic. <c>Truncated</c> reflects the underlying
/// artifact's own durable capture-truncation fact and is populated only for <c>"Ok"</c>.
/// </summary>
public sealed record SealedAgentArtifactWindowResponse(
    string Status, string Text, long NextOffset, long TotalLengthSoFar, bool? Truncated);
