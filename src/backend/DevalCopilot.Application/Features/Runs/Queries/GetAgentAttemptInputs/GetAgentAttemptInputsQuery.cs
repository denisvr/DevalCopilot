using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputs;

/// <summary>
/// The recorded collaboration-input references of exactly one Agent attempt of a run (ADR-0051), independent of any message the attempt
/// produced. It reads bounded scalar projections of that attempt, of at most 65 of its ordered input rows and of at most the 64 messages
/// they reference inside the same run; never a prompt, summary, content JSON, artifact, provider session or any reply, repair or input
/// chain. It writes, claims and invokes nothing. An unknown run, a missing or foreign attempt and a non-Agent attempt fail with
/// <c>agent_attempt_inputs.not_found</c>. <paramref name="Limit"/> (ADR-0053) bounds only the returned prefix: absent, it is
/// <see cref="DefaultLimit"/>; otherwise 1 to <see cref="MaximumLimit"/>, refused and never clamped outside that range. The whole
/// inspected set is proved whatever the limit.
/// </summary>
public sealed record GetAgentAttemptInputsQuery(Guid RunId, Guid AttemptId, int? Limit = null)
    : IQuery<Result<GetAgentAttemptInputsQueryResult>>
{
    public const int DefaultLimit = 10;

    public const int MaximumLimit = 64;
}
