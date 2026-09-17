namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One immutable, project-owned collaboration fact. It stores bounded semantic evidence,
/// never a provider-native transcript, configuration, executable path, or credential.
/// </summary>
public sealed class CollaborationMessage
{
    public const string ProtocolVersionOne = "1.0";

    private CollaborationMessage()
    {
    }

    public static CollaborationMessage Record(
        Guid id,
        Guid runId,
        Guid? attemptId,
        string protocolVersion,
        ParticipantKind actor,
        ParticipantKind recipient,
        CollaborationMessageType type,
        Guid? inReplyToMessageId,
        string summary,
        string structuredContentJson,
        CollaborationMessageProvenance provenance,
        DateTimeOffset occurredAtUtc)
    {
        if (id == Guid.Empty || runId == Guid.Empty)
        {
            throw new ArgumentException("A collaboration message requires durable identifiers.");
        }

        if (!string.Equals(protocolVersion, ProtocolVersionOne, StringComparison.Ordinal))
        {
            throw new ArgumentException("The collaboration protocol version is not supported.", nameof(protocolVersion));
        }

        if (!Enum.IsDefined(actor) || actor == ParticipantKind.None || !Enum.IsDefined(recipient) || recipient == ParticipantKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(actor));
        }

        if (actor == recipient || !Enum.IsDefined(type) || !Enum.IsDefined(provenance))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        if (inReplyToMessageId == id)
        {
            throw new ArgumentException("A collaboration message cannot reply to itself.", nameof(inReplyToMessageId));
        }

        var replyViolation = CollaborationMessageReplyPolicy.EvaluateReference(type, inReplyToMessageId.HasValue);
        if (replyViolation != CollaborationMessageReplyViolation.None)
        {
            throw new ArgumentException("The collaboration reply is not valid for this message type.", nameof(inReplyToMessageId));
        }

        if (!CollaborationMessageContentPolicy.IsSafeSummary(summary))
        {
            throw new ArgumentException("The collaboration summary is invalid.", nameof(summary));
        }

        ValidateActorForType(actor, type);
        CollaborationMessageContentPolicy.Validate(type, structuredContentJson);

        return new CollaborationMessage
        {
            Id = id,
            RunId = runId,
            AttemptId = attemptId,
            ProtocolVersion = protocolVersion,
            Actor = actor,
            Recipient = recipient,
            Type = type,
            InReplyToMessageId = inReplyToMessageId,
            Summary = summary,
            StructuredContentJson = structuredContentJson,
            Provenance = provenance,
            OccurredAtUtc = occurredAtUtc,
        };
    }

    public long Sequence { get; private set; }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid? AttemptId { get; private set; }

    public string ProtocolVersion { get; private set; } = string.Empty;

    public ParticipantKind Actor { get; private set; }

    public ParticipantKind Recipient { get; private set; }

    public CollaborationMessageType Type { get; private set; }

    public Guid? InReplyToMessageId { get; private set; }

    public string Summary { get; private set; } = string.Empty;

    public string StructuredContentJson { get; private set; } = string.Empty;

    public CollaborationMessageProvenance Provenance { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    private static void ValidateActorForType(ParticipantKind actor, CollaborationMessageType type)
    {
        var allowed = type switch
        {
            CollaborationMessageType.Proposal => actor is ParticipantKind.Codex or ParticipantKind.Claude or ParticipantKind.Human,
            CollaborationMessageType.Acceptance => actor is ParticipantKind.Codex or ParticipantKind.Claude,
            CollaborationMessageType.Challenge => actor is ParticipantKind.Codex or ParticipantKind.Claude or ParticipantKind.Human,
            CollaborationMessageType.Question => actor is ParticipantKind.Codex or ParticipantKind.Claude or ParticipantKind.Human,
            CollaborationMessageType.Decision => actor is ParticipantKind.Codex or ParticipantKind.Human,
            CollaborationMessageType.ExecutionReport => actor is ParticipantKind.Codex or ParticipantKind.Claude or ParticipantKind.Orchestrator,
            CollaborationMessageType.ReviewFinding => actor is ParticipantKind.Codex or ParticipantKind.Claude or ParticipantKind.Human,
            CollaborationMessageType.RevisionResponse => actor is ParticipantKind.Codex or ParticipantKind.Claude,
            CollaborationMessageType.Escalation => actor is ParticipantKind.Codex or ParticipantKind.Claude or ParticipantKind.Orchestrator,
            _ => false,
        };

        if (!allowed)
        {
            throw new ArgumentException("The actor cannot emit this collaboration message type.", nameof(actor));
        }
    }
}
