using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationEvidence;

/// <summary>The immutable recorded identity of the inspected attempt, the same header ADR-0051 shows: identity, number, role, provider
/// and response contract. It carries no status, time, outcome, assignment, session or path.</summary>
public sealed record AgentAttemptVerificationEvidenceHeader(
    Guid Id, int AttemptNumber, AgentRole Role, AgentProvider Provider, AgentResponseContract ResponseContract);
