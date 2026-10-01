namespace DevalCopilot.Api.Features.Runs.AuthorizePlanningImplementation;

/// <summary>The one caller-supplied value of the human implementation authorization: a required, bounded rationale
/// (at most 600 characters). The server normalizes and validates it; it is advisory context, never an instruction to the
/// host, and the instruction, final plan, and every other fact are derived from the persisted escalation.</summary>
public sealed record AuthorizePlanningImplementationRequest(string Rationale);
