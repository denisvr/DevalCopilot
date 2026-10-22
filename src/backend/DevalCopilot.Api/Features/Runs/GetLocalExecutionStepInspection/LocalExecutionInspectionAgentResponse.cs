namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionStepInspection;

/// <summary>The immutable recorded identity of the linked Agent attempt, the same header the existing selected-attempt inspections show:
/// identity, number, role, provider and response contract. It carries no status, time, outcome, assignment, session, message, artifact or
/// process fact; the explicit Agent evidence readers own those reads.</summary>
public sealed record LocalExecutionInspectionAgentResponse(
    Guid Id, int AttemptNumber, string Role, string Provider, string ResponseContract);
