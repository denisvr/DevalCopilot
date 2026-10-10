using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunEventHistory;

/// <summary>The recorded actor columns of one event header, classified without materializing an unknown stored value. A null
/// <see cref="Kind"/> says the stored combination is not one this version recognizes (with a null role and provider); an Agent may
/// carry a null <see cref="Role"/> exactly as <see cref="ParticipantIdentity.ForAgentWithUnknownRole"/> records it. Nothing is
/// inferred: a role, provider or authority that was not recorded is never supplied.</summary>
public sealed record RunEventHistoryActor(ParticipantKind? Kind, AgentRole? Role, AgentProvider? Provider);
