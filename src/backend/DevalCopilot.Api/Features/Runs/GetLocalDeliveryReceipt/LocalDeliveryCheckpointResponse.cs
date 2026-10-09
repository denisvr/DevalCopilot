namespace DevalCopilot.Api.Features.Runs.GetLocalDeliveryReceipt;

public sealed record LocalDeliveryCheckpointResponse(Guid Id, int Number, string FingerprintSha256, int ChangedPathCount);
