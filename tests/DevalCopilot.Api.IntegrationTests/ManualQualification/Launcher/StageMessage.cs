namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// One produced collaboration message the timeline listed for a stage's accepted attempt, with the facts the verifier and the safe
/// summary need and nothing else: opaque identities, closed type, actor and provenance names, and the two comparisons made for it.
/// It never holds a summary, structured content or any provider text.
/// </summary>
/// <param name="Id">The opaque message identity.</param>
/// <param name="Type">The closed message type name the timeline listed.</param>
/// <param name="InReplyToMessageId">The message this one replies to, when any.</param>
/// <param name="AttemptId">The attempt the timeline attributed the message to, when any.</param>
/// <param name="Actor">The closed provider and role names of the author, joined by a dot.</param>
/// <param name="Provenance">The closed provenance name the timeline listed.</param>
/// <param name="EvidenceRouteComparison">Whether the public message-evidence route was compared with the stored attempt for this
/// message, and with what result. It is made only inside the bound of <see cref="StageMessageBounds"/>.</param>
/// <param name="PersistedMetadataComparison">Whether the stored message row of the accepted attempt agrees with this timeline entry
/// on type, author, provenance, reply and attempt.</param>
public sealed record StageMessage(
    Guid Id,
    string? Type,
    Guid? InReplyToMessageId,
    Guid? AttemptId,
    string? Actor,
    string? Provenance,
    EvidenceRouteComparisonStatus EvidenceRouteComparison,
    EvidenceRouteComparisonStatus PersistedMetadataComparison);
