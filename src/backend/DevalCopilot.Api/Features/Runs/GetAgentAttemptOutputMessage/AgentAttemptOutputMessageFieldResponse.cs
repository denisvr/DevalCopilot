namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptOutputMessage;

/// <summary>One validated structured-content field of an available message: a name from the closed schema of its type and a bounded,
/// safe text value.</summary>
public sealed record AgentAttemptOutputMessageFieldResponse(string Name, string Value);
