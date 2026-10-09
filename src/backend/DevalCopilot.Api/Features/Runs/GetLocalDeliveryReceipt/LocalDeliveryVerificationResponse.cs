namespace DevalCopilot.Api.Features.Runs.GetLocalDeliveryReceipt;

/// <summary>One recorded verification member in recorded order, named by the immutable snapshot of its execution.</summary>
public sealed record LocalDeliveryVerificationResponse(
    int Order,
    Guid CommandId,
    Guid ExecutionId,
    int ExecutionNumber,
    string CommandName,
    string Status,
    int ExitCode,
    DateTimeOffset CompletedAtUtc);
