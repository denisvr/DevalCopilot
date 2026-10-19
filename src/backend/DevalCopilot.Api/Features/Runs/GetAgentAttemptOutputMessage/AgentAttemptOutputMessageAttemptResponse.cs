namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptOutputMessage;

/// <summary>The immutable recorded identity of the inspected attempt. It carries no status, time, outcome, assignment, session or path.</summary>
/// <param name="AttemptNumber">The recorded, positive attempt number of its run.</param>
/// <param name="Role">A supported Agent role name.</param>
/// <param name="Provider">The provider name supported for that role.</param>
/// <param name="ResponseContract">The response contract name that belongs to the role.</param>
public sealed record AgentAttemptOutputMessageAttemptResponse(Guid Id, int AttemptNumber, string Role, string Provider, string ResponseContract);
