namespace DevalCopilot.Api.Features.Runs.GetRunEventHistory;

/// <param name="Kind">None, Orchestrator, Agent, Human, or the fixed value <c>Unrecognized</c>.</param>
/// <param name="Role">A recorded Agent role; null when none was recorded, and always null for an unrecognized actor.</param>
/// <param name="Provider">A recorded Agent provider; null for a non-Agent and for an unrecognized actor.</param>
public sealed record RunEventHistoryActorResponse(string Kind, string? Role, string? Provider);
