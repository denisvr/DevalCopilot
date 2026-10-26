using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationOutputWindow;

/// <summary>
/// One bounded, integrity-verified window of one sealed output stream of one recorded project verification execution (ADR-0069). It
/// re-proves the selected execution's historical ownership and result facts on every request, resolves only the requested stream's row
/// and reads its bytes only through the artifact store's sealed verification; never a partial capture, another stream or a project or
/// workspace file. <paramref name="FromOffset"/> is a UTF-8 byte cursor from 0 to 2^53 - 1 and <paramref name="MaxBytes"/> a cap from
/// <see cref="MinimumMaxBytes"/> to <see cref="MaximumMaxBytes"/>; anything outside is refused, never clamped. An unknown project, an
/// absent or foreign execution and an unsupported stream fail with <c>project_verification_inspection.not_found</c>.
/// </summary>
public sealed record GetProjectVerificationOutputWindowQuery(
    Guid ProjectId, Guid ExecutionId, VerificationOutputPurpose Stream, long FromOffset, int MaxBytes)
    : IQuery<Result<GetProjectVerificationOutputWindowQueryResult>>
{
    public const int DefaultMaxBytes = 16 * 1024;

    public const int MinimumMaxBytes = 64;

    public const int MaximumMaxBytes = 64 * 1024;

    public const long MaximumOffset = (1L << 53) - 1;
}
