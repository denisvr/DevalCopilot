using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalDeliveryReceipt;

/// <summary>Read-only historical receipt of a run's recorded local delivery (ADR-0032). It reads persisted facts only: no Git, filesystem,
/// process or provider state, and it writes, claims and repairs nothing.</summary>
public sealed record GetLocalDeliveryReceiptQuery(Guid RunId) : IQuery<Result<GetLocalDeliveryReceiptQueryResult>>;
