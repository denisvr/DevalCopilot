using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputMessage;

/// <summary>
/// The recorded content of exactly one collaboration message that a proved output set of one Agent attempt contains (ADR-0054). The
/// whole bounded set is proved again first, exactly as the reference inspection proves it; only then is that one member's guarded summary
/// and content read inside the same run and attempt association, through the unchanged ADR-0035/0036 admission. No other message's text,
/// reply, artifact, raw JSON or provider session is read. It writes, claims and invokes nothing. An unknown run, a missing, foreign or
/// non-Agent attempt and a message that is not a member of a proved set fail with <c>agent_attempt_output_message.not_found</c>.
/// </summary>
public sealed record GetAgentAttemptOutputMessageQuery(Guid RunId, Guid AttemptId, Guid MessageId)
    : IQuery<Result<GetAgentAttemptOutputMessageQueryResult>>;
