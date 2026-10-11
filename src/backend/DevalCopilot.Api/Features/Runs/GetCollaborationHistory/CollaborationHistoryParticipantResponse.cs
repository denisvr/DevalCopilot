namespace DevalCopilot.Api.Features.Runs.GetCollaborationHistory;

/// <param name="Kind">Orchestrator, Agent, Human, or the fixed value <c>Unrecognized</c>.</param>
/// <param name="Role">A recorded Agent role; null when none was recorded, and always null for an unrecognized participant.</param>
/// <param name="Provider">A recorded Agent provider; null for a non-Agent and for an unrecognized participant.</param>
public sealed record CollaborationHistoryParticipantResponse(string Kind, string? Role, string? Provider);
