namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalDeliveryReceipt;

/// <summary>One recorded verification member, named by the immutable command snapshot of its execution (never the current recipe).</summary>
public sealed record LocalDeliveryVerificationView(
    int Order,
    Guid CommandId,
    Guid ExecutionId,
    int ExecutionNumber,
    string CommandName,
    string Status,
    int ExitCode,
    DateTimeOffset CompletedAtUtc);
