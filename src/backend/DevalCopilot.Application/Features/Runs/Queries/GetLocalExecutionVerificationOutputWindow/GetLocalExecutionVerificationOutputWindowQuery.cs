using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionVerificationOutputWindow;

/// <summary>
/// One bounded, integrity-verified window of one sealed output stream of the verification a recorded step links (ADR-0061). It re-proves
/// the whole bounded structure and the selected verification and pinned-recipe association before any artifact is opened, resolves only
/// the requested stream's row and reads bytes only through the artifact store's sealed verification, under ADR-0052's unchanged storage
/// admission, integrity, descriptor and UTF-8 cursor rules. <paramref name="FromOffset"/> is a UTF-8 byte cursor from 0 to 2^53 - 1 and
/// <paramref name="MaxBytes"/> a cap from <see cref="MinimumMaxBytes"/> to <see cref="MaximumMaxBytes"/>; anything outside is refused,
/// never clamped. An unknown run, a run without a sequence, a step outside it, a non-verification step and an unsupported stream fail
/// with <c>local_execution_inspection.not_found</c>.
/// </summary>
public sealed record GetLocalExecutionVerificationOutputWindowQuery(
    Guid RunId, Guid StepId, VerificationOutputPurpose Stream, long FromOffset, int MaxBytes)
    : IQuery<Result<GetLocalExecutionVerificationOutputWindowQueryResult>>
{
    public const int DefaultMaxBytes = 16 * 1024;

    public const int MinimumMaxBytes = 64;

    public const int MaximumMaxBytes = 64 * 1024;

    public const long MaximumOffset = (1L << 53) - 1;
}
