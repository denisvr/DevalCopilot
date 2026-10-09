namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalDeliveryReceipt;

public sealed record LocalDeliveryCheckpointView(Guid Id, int Number, string FingerprintSha256, int ChangedPathCount);
