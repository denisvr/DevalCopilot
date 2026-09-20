namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The complete, closed identity of one participant in a run's durable timeline or collaboration
/// protocol: a neutral <see cref="Kind"/> (None/Orchestrator/Agent/Human), plus — only for an
/// Agent — the semantic <see cref="Role"/> it occupied and the <see cref="Provider"/> that
/// produced it. Provider is provenance, never authority: no code may grant collaboration-message
/// authorization from <see cref="Provider"/> alone; that remains solely
/// <see cref="CollaborationMessageAuthorPolicy"/>, keyed by <see cref="Role"/>.
///
/// Two legitimate Agent shapes exist:
/// <list type="bullet">
/// <item><description><see cref="ForAgent"/> — a real, provider-observed author: role AND
/// provider both known. This is the only shape <see cref="CollaborationMessage.RecordAgent"/>
/// may construct.</description></item>
/// <item><description><see cref="ForAgentWithUnknownRole"/> — migrated/recipient compatibility
/// metadata or a simulated persona: a provider fact recorded with no accompanying role, because
/// none was ever truthfully known for that side of the fact.</description></item>
/// </list>
/// Equality compares the complete identity (Kind + Role + Provider) via record value equality,
/// so two Agent identities with the same provider but different roles are never treated as equal.
/// </summary>
public sealed record ParticipantIdentity
{
    private ParticipantIdentity(ParticipantKind kind, AgentRole? role, AgentProvider? provider)
    {
        Kind = kind;
        Role = role;
        Provider = provider;
    }

    public ParticipantKind Kind { get; }

    public AgentRole? Role { get; }

    public AgentProvider? Provider { get; }

    public static ParticipantIdentity None() => new(ParticipantKind.None, role: null, provider: null);

    public static ParticipantIdentity ForOrchestrator() => new(ParticipantKind.Orchestrator, role: null, provider: null);

    public static ParticipantIdentity ForHuman() => new(ParticipantKind.Human, role: null, provider: null);

    /// <summary>
    /// A real, provider-observed Agent author: both <paramref name="role"/> and
    /// <paramref name="provider"/> are truthfully known and fully populated. This is the only
    /// factory that may back a <see cref="CollaborationMessageProvenance.ProviderObserved"/> fact.
    /// </summary>
    public static ParticipantIdentity ForAgent(AgentRole role, AgentProvider provider)
    {
        RequireDefinedRole(role);
        RequireDefinedProvider(provider);
        return new ParticipantIdentity(ParticipantKind.Agent, role, provider);
    }

    /// <summary>
    /// An Agent identity for which only the provider is truthfully known, including a simulated
    /// persona, a message recipient, or a migrated row without a provable owning role.
    /// <see cref="Role"/> is always null — never inferred. The owning entity's provenance and
    /// attempt kind distinguish those contexts; two factory names returning the same value would
    /// not create distinct identities.
    /// </summary>
    public static ParticipantIdentity ForAgentWithUnknownRole(AgentProvider provider)
    {
        RequireDefinedProvider(provider);
        return new ParticipantIdentity(ParticipantKind.Agent, role: null, provider);
    }

    /// <summary>
    /// Reconstructs an identity from flat, already-persisted fields, enforcing every invariant
    /// this type owns. Used by entities that store Kind/Role/Provider as separate flat columns and
    /// need to recompose the whole value for callers (mirrors <c>Attempt.AgentEffect</c>'s
    /// "recompute from stored primitives" pattern).
    /// </summary>
    public static ParticipantIdentity FromParts(ParticipantKind kind, AgentRole? role, AgentProvider? provider)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a defined participant kind.");
        }

        if (role.HasValue)
        {
            RequireDefinedRole(role.Value);
        }

        if (provider.HasValue)
        {
            RequireDefinedProvider(provider.Value);
        }

        switch (kind)
        {
            case ParticipantKind.None:
            case ParticipantKind.Human:
            case ParticipantKind.Orchestrator:
                if (role.HasValue || provider.HasValue)
                {
                    throw new ArgumentException(
                        $"{kind} participants must not carry an agent role or provider.", nameof(kind));
                }

                break;

            case ParticipantKind.Agent:
                if (!provider.HasValue)
                {
                    throw new ArgumentException("An Agent participant requires a provider.", nameof(provider));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a defined participant kind.");
        }

        return new ParticipantIdentity(kind, role, provider);
    }

    private static void RequireDefinedRole(AgentRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Not a defined agent role.");
        }
    }

    private static void RequireDefinedProvider(AgentProvider provider)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider), provider, "Not a defined agent provider.");
        }
    }
}
