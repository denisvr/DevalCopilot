using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputMessage;

/// <summary>
/// The recorded content of exactly one collaboration message that a proved input set of one Agent attempt references (ADR-0053). The
/// whole bounded input set is proved again first, exactly as the reference inspection proves it (ADR-0051); only then is that one
/// member's guarded summary and content read inside the same run, through the unchanged ADR-0035/0036 admission. No other message's
/// text, no reply, producing attempt, artifact, raw JSON or provider session is read. It writes, claims and invokes nothing. An
/// unknown run, a missing, foreign or non-Agent attempt and a message that is not a member of a proved set fail with
/// <c>agent_attempt_input_message.not_found</c>.
/// </summary>
public sealed record GetAgentAttemptInputMessageQuery(Guid RunId, Guid AttemptId, Guid MessageId)
    : IQuery<Result<GetAgentAttemptInputMessageQueryResult>>;
