namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalDeliveryReceipt;

/// <summary>The selected Human checkpoint review the operation was pinned to. It is not an audit of every Human decision.</summary>
public sealed record LocalDeliveryHumanReviewView(Guid ReviewId, string Decision);
