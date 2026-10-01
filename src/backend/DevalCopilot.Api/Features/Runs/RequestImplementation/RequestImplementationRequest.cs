namespace DevalCopilot.Api.Features.Runs.RequestImplementation;

/// <summary>The resolved plan to implement and optional direct human guidance. The server normalizes and bounds
/// the guidance; it is advisory clarification of the authorized plan, never an instruction to the host. Omitted
/// or null means no guidance.</summary>
public sealed record RequestImplementationRequest(Guid PlanProposalMessageId, string? Guidance = null);
