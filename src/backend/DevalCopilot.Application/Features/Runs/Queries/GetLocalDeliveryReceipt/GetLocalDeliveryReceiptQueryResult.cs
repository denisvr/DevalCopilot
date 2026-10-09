namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalDeliveryReceipt;

/// <param name="State">One of the four closed states.</param>
/// <param name="Receipt">Present only for <see cref="LocalDeliveryReceiptState.Available"/>; never a partial or substituted receipt.</param>
public sealed record GetLocalDeliveryReceiptQueryResult(LocalDeliveryReceiptState State, LocalDeliveryReceiptView? Receipt);
