using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCodexAccountUsageWarning;

/// <summary>
/// One explicit, advisory check of the run's saved Codex account-usage warning (ADR-0026). It reads the saved setting and the vetted
/// Codex launch afresh, makes at most one bounded strict observation of the host's Codex account outside any transaction, re-reads
/// both afterwards, and reports one dated advisory outcome. Nothing a caller supplies influences the threshold, the executable or the
/// observation; the check writes nothing and is never read by a claim, gate or invocation path.
/// </summary>
public sealed record GetCodexAccountUsageWarningQuery(Guid RunId) : IQuery<Result<GetCodexAccountUsageWarningQueryResult>>;
