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
        ParticipantIdentity actor,
        ParticipantIdentity recipient,
        CollaborationMessageType type,
        Guid? inReplyToMessageId,
        string summary,
        string structuredContentJson,
        CollaborationMessageProvenance provenance,
        DateTimeOffset occurredAtUtc) =>
        RecordCore(
            id, runId, attemptId, protocolVersion, actor, recipient, type, inReplyToMessageId, summary, structuredContentJson,
            provenance, occurredAtUtc,
            authorValidation: () => ValidateActorForType(actor, type));

    /// <summary>
    /// The single factory for a real Agent attempt's own collaboration output. Receives the actual
    /// Domain <see cref="Attempt"/> rather than separately caller-supplied identifiers, role,
    /// provider, response contract, protocol version, or provenance — every one of those facts is
    /// derived from the attempt itself, so no caller can select a role/provider/attempt
    /// combination that does not truthfully exist. Authorization is decided solely by
    /// <see cref="CollaborationMessageAuthorPolicy"/> against the attempt's own
    /// <see cref="Runs.AgentRole"/> — this factory never calls <see cref="ValidateActorForType"/>,
    /// the legacy participant-kind gate reserved for <see cref="Record"/>'s Human, Orchestrator,
    /// and Simulated callers.
    /// </summary>
    public static CollaborationMessage RecordAgent(
        Attempt attempt,
        Guid id,
        ParticipantIdentity recipient,
        CollaborationMessageType type,
        Guid? inReplyToMessageId,
        string summary,
        string structuredContentJson,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (attempt.Kind != AttemptKind.Agent)
        {
            throw new ArgumentException("RecordAgent requires an Agent attempt.", nameof(attempt));
        }

        if (attempt.Id == Guid.Empty || attempt.RunId == Guid.Empty)
        {
            throw new ArgumentException("An Agent attempt requires durable identifiers.", nameof(attempt));
        }

        if (attempt.AgentRole is not { } role
            || attempt.AgentProvider is not { } provider
            || attempt.AgentResponseContract is not { } responseContract
            || attempt.AgentProtocolVersion is not { } protocolVersion)
        {
            throw new ArgumentException(
                "An Agent attempt must have a defined role, provider, response contract, and protocol version.", nameof(attempt));
        }

        // Resolve and verify coherence rather than assume it — mirrors Attempt.CompleteImplementation's
        // own "resolve and verify" reasoning: the role/response-contract pairing is always coherent
        // today because every ClaimAgent* factory sets both from the same AgentAttemptContract, but
        // this factory never trusts that silently.
        if (AgentAttemptContract.For(role).ResponseContract != responseContract)
        {
            throw new ArgumentException("The attempt's role and response contract are not coherent.", nameof(attempt));
        }

        // The dispatch marker, not Attempt.Status, is the authoritative proof that a real provider
        // invocation could have produced provider-observed output: a role-specific result handler
        // may apply the terminal Domain transition before constructing the collaboration message(s)
        // in the same atomic unit, so Status may already be Completed/Failed by the time this is
        // called.
        if (!attempt.AgentDispatchedAtUtc.HasValue)
        {
            throw new ArgumentException(
                "A collaboration message can only be recorded for an attempt that was actually dispatched to its provider.",
                nameof(attempt));
        }

        var actor = ParticipantIdentity.ForAgent(role, provider);

        return RecordCore(
            id, attempt.RunId, attempt.Id, protocolVersion, actor, recipient, type, inReplyToMessageId, summary,
            structuredContentJson, CollaborationMessageProvenance.ProviderObserved, occurredAtUtc,
            authorValidation: () =>
            {
                if (!CollaborationMessageAuthorPolicy.AllowedMessageTypes(role).Contains(type))
                {
                    throw new ArgumentException("This role is not authorized to author this message type.", nameof(type));
                }
            });
    }

    /// <summary>
    /// The shared envelope validation and construction both <see cref="Record"/> and
    /// <see cref="RecordAgent"/> use — common pre-author validations (identifiers, participant/type/
    /// provenance definedness, reply-reference shape, summary safety), then the caller's own
    /// factory-specific author validation (the legacy participant policy for <see cref="Record"/>,
    /// the role policy for <see cref="RecordAgent"/>), then content-policy validation, then entity
    /// construction — in exactly this order, matching <see cref="Record"/>'s own pre-extraction
    /// order exactly so its observable validation and exception behavior are unchanged.
    /// </summary>
    private static CollaborationMessage RecordCore(
        Guid id,
        Guid runId,
        Guid? attemptId,
        string protocolVersion,
        ParticipantIdentity actor,
        ParticipantIdentity recipient,
        CollaborationMessageType type,
        Guid? inReplyToMessageId,
        string summary,
        string structuredContentJson,
        CollaborationMessageProvenance provenance,
        DateTimeOffset occurredAtUtc,
        Action authorValidation)
    {
        if (id == Guid.Empty || runId == Guid.Empty)
        {
            throw new ArgumentException("A collaboration message requires durable identifiers.");
        }

        if (!string.Equals(protocolVersion, ProtocolVersionOne, StringComparison.Ordinal))
        {
            throw new ArgumentException("The collaboration protocol version is not supported.", nameof(protocolVersion));
        }

        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(recipient);

        if (actor.Kind == ParticipantKind.None || recipient.Kind == ParticipantKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(actor));
        }

        if (actor == recipient || !Enum.IsDefined(type) || !Enum.IsDefined(provenance))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        if (provenance == CollaborationMessageProvenance.ProviderObserved
            && (actor.Kind != ParticipantKind.Agent || !actor.Role.HasValue))
        {
            throw new ArgumentException("Provider-observed messages require a role-bound Agent actor.", nameof(actor));
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

        authorValidation();
        CollaborationMessageContentPolicy.Validate(type, structuredContentJson);

        return new CollaborationMessage
        {
            Id = id,
            RunId = runId,
            AttemptId = attemptId,
            ProtocolVersion = protocolVersion,
            ActorKind = actor.Kind,
            ActorAgentRole = actor.Role,
            ActorAgentProvider = actor.Provider,
            RecipientKind = recipient.Kind,
            RecipientAgentRole = recipient.Role,
            RecipientAgentProvider = recipient.Provider,
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

    public ParticipantKind ActorKind { get; private set; }

    public AgentRole? ActorAgentRole { get; private set; }

    public AgentProvider? ActorAgentProvider { get; private set; }

    public ParticipantIdentity Actor => ParticipantIdentity.FromParts(ActorKind, ActorAgentRole, ActorAgentProvider);

    public ParticipantKind RecipientKind { get; private set; }

    public AgentRole? RecipientAgentRole { get; private set; }

    public AgentProvider? RecipientAgentProvider { get; private set; }

    public ParticipantIdentity Recipient =>
        ParticipantIdentity.FromParts(RecipientKind, RecipientAgentRole, RecipientAgentProvider);

    public CollaborationMessageType Type { get; private set; }

    public Guid? InReplyToMessageId { get; private set; }

    public string Summary { get; private set; } = string.Empty;

    public string StructuredContentJson { get; private set; } = string.Empty;

    public CollaborationMessageProvenance Provenance { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    private static void ValidateActorForType(ParticipantIdentity actor, CollaborationMessageType type)
    {
        var isAgent = actor.Kind == ParticipantKind.Agent;
        var isCodexPersona = isAgent && actor.Provider == AgentProvider.Codex;
        var isHuman = actor.Kind == ParticipantKind.Human;
        var isOrchestrator = actor.Kind == ParticipantKind.Orchestrator;
        var allowed = type switch
        {
            CollaborationMessageType.Proposal => isAgent || isHuman,
            CollaborationMessageType.Acceptance => isAgent,
            CollaborationMessageType.Challenge => isAgent || isHuman,
            CollaborationMessageType.Question => isAgent || isHuman,
            CollaborationMessageType.Decision => isCodexPersona || isHuman,
            CollaborationMessageType.ExecutionReport => isAgent || isOrchestrator,
            CollaborationMessageType.ReviewFinding => isAgent || isHuman,
            CollaborationMessageType.RevisionResponse => isAgent,
            CollaborationMessageType.Escalation => isAgent || isOrchestrator,
            CollaborationMessageType.ReviewApproval => isAgent || isHuman,
            _ => false,
        };

        if (!allowed)
        {
            throw new ArgumentException("The actor cannot emit this collaboration message type.", nameof(actor));
        }
    }
}
