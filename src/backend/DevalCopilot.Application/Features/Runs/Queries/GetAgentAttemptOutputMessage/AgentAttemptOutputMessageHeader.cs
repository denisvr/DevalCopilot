using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputMessage;

/// <summary>The immutable recorded identity of the inspected attempt: identity, number, role, provider and response contract. It
/// carries no status, time, outcome, assignment, session or path.</summary>
public sealed record AgentAttemptOutputMessageHeader(
    Guid Id, int AttemptNumber, AgentRole Role, AgentProvider Provider, AgentResponseContract ResponseContract);
