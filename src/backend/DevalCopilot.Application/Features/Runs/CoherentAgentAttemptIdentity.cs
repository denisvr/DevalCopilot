using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>The recorded status and immutable role, provider and response contract of an Agent attempt whose scalar storage the
/// unchanged assignment policy and identity rule accept. It states recorded identity only, never current execution or authority.</summary>
internal sealed record CoherentAgentAttemptIdentity(
    AttemptStatus Status, AgentRole Role, AgentProvider Provider, AgentResponseContract ResponseContract);
