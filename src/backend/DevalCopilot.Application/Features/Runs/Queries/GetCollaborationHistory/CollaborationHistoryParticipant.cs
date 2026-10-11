using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;

/// <summary>The recorded actor or recipient columns of one message, classified without materializing an unknown stored value. A null
/// <see cref="Kind"/> says the stored combination is not one a message can carry (an unknown or inconsistent value, or
/// <c>None</c>), with a null role and provider. An Agent may carry a null <see cref="Role"/> exactly as
/// <see cref="ParticipantIdentity.ForAgentWithUnknownRole"/> records it; nothing is inferred.</summary>
public sealed record CollaborationHistoryParticipant(ParticipantKind? Kind, AgentRole? Role, AgentProvider? Provider);
