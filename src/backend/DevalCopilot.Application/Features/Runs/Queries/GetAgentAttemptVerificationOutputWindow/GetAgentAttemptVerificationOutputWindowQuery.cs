using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationOutputWindow;

/// <summary>
/// One bounded, integrity-verified window of one sealed output stream of one recorded verification member of exactly one CodeReviewer
/// attempt (ADR-0052). It re-proves the whole membership and the execution's historical ownership, resolves only the requested stream's
/// row and reads its bytes only through the artifact store's sealed verification; never a partial capture, another stream or a source
/// file. <paramref name="FromOffset"/> is a UTF-8 byte cursor from 0 to 2^53 - 1 and <paramref name="MaxBytes"/> a cap from
/// <see cref="MinimumMaxBytes"/> to <see cref="MaximumMaxBytes"/>; anything outside is refused, never clamped. An unknown run, a missing,
/// foreign or non-Agent attempt and an execution that is not a proved member fail with <c>agent_attempt_verification.not_found</c>.
/// </summary>
public sealed record GetAgentAttemptVerificationOutputWindowQuery(
    Guid RunId, Guid AttemptId, Guid ExecutionId, VerificationOutputPurpose Stream, long FromOffset, int MaxBytes)
    : IQuery<Result<GetAgentAttemptVerificationOutputWindowQueryResult>>
{
    public const int DefaultMaxBytes = 16 * 1024;

    public const int MinimumMaxBytes = 64;

    public const int MaximumMaxBytes = 64 * 1024;

    public const long MaximumOffset = (1L << 53) - 1;
}
