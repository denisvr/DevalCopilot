namespace DevalCopilot.Api.Features.Runs.GetLocalDeliveryReceipt;

/// <summary>One of NotRecorded, NotCompleted, Unavailable or Available. <c>Receipt</c> is present only for Available and is never a
/// partial or substituted receipt. It describes a recorded local delivery, never the current workspace or a remote publication.</summary>
public sealed record GetLocalDeliveryReceiptResponse(string State, LocalDeliveryReceiptResponse? Receipt);
